using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;

namespace Kape22Importer.Persistence;

// Story 2.8 (FR-11), replaced by Story 4.6 (FR-21, AD-1/AD-6): turns a Kape22ImportBundleMapper.Map
// result into rows in AscoLSI. On a bundle whose FR-20 controls all pass (bundle.Success): the
// anti-duplicate guard (D22) checks for an existing "<NumeroFichier> — OK" L_D_LOG_COMMANDE row for the
// same NumeroFichier + OF; if none, and - for a cold Coulee only - its L_D_COULEE row already exists,
// L_D_KAPE22 + the "— OK" L_D_LOG_COMMANDE row + every non-null downstream entity from the bundle are
// committed together in exactly one SaveChanges (AC-FR21-1, AD-1). A Coulee already on file (several OF
// routinely dispatch from the same cast) is reused rather than re-inserted, hot or cold (Spec Change Log,
// 2026-09-16). If the guard finds a prior success, nothing is inserted and the Fichier comes back as an
// already-imported skip: AlreadyImported true, Success true, InsertedId null, empty Errors (AC-FR11-6).
// A cold Coulee whose L_D_COULEE row is missing
// is rejected instead, with a REJETÉ log row citing the missing Coulee (AC-FR20-5).
// A same-bundle L_D_CONSIGNES natural-key collision (two sections sharing the same CodeOperation for
// this OF) is rejected the same way, with a REJETÉ log row citing the colliding CodeOperation, before
// ConsignesRows.AddRange is ever called (A-5, Epic 4 retro).
// On a bundle that already carries Errors (Kape22Mapper.Map failed upstream, or one of
// Kape22ImportBundleMapper's own FR-20 controls tripped): no L_D_KAPE22 row; a single
// "<NumeroFichier> — REJETÉ : <summary>" L_D_LOG_COMMANDE row in its own transaction when the OF is
// readable (AC-FR11-4), nothing at all when it is not (D15).
// A SQL failure (DbUpdateException or DbException) is caught and returned as a PersistenceError; the
// failed SaveChanges has already rolled back its transaction, so none of the staged rows persist
// (AC-FR11-5, extended by AC-FR21-2). Other exception types are not swallowed. The connection string
// reaches the DbContext through configuration and is never hard-coded (AC-FR11-8, CC-7).
// The DbContext lifetime and the Epic 3 host wiring are the orchestrator's concern; this type is
// constructed per Fichier with a fresh context.
public sealed class Kape22Persister(AscoLsiDbContext context, IConfiguration configuration, TimeProvider? timeProvider = null)
{
    // L_D_LOG_COMMANDE.Commande is a format variation point (AC-FR16-2): it comes from configuration,
    // falling back to the P60 command for the reference template.
    private const string CommandeKey = "Import:Commande";

    private const string DefaultCommande = "P60";

    // L_D_LOG_COMMANDE.User comes from configuration, never a literal (CC-7). The machine name is a
    // last-resort fallback so the NOT NULL column is always satisfiable.
    private const string InitiatingServerKey = "Import:InitiatingServer";

    // The L_D_LOG_COMMANDE.Message suffix that marks a committed success. The anti-duplicate guard keys
    // on it, so Story 3.3 must keep it stable when it finalizes the log wording.
    private const string OkMessageSuffix = " — OK";

    // Import:InitiatingServer, resolved and length-checked once at construction (see ResolveUser).
    private readonly string user = ResolveUser(configuration);

    public ImportResult Persist(Kape22ImportBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return bundle.Success
            ? PersistMapped(bundle)
            : PersistRejected(bundle);
    }

    private ImportResult PersistMapped(Kape22ImportBundle bundle)
    {
        L_D_KAPE22 entity = bundle.Kape22!;

        // Map always populates both fields on a deserialized Fichier, and this path is success-only.
        string numeroFichier = bundle.NumeroFichier!;
        string of = bundle.OF!;

        try
        {
            // AC-FR11-6/11-7 (D22): a committed "<NumeroFichier> — OK" row for this NumeroFichier + OF
            // means the Fichier already imported (a crash between the commit and the file move). Skip
            // it: no new row, InsertedId stays null, Success stays true, AlreadyImported flags the skip
            // so the orchestrator archives the Fichier and logs the "already imported" warning.
            if (OkLogRowExists(numeroFichier, of))
            {
                return new ImportResult { AlreadyImported = true, Warnings = bundle.Warnings };
            }

            // AC-FR20-5: a cold Coulee (CodeConsignePits == "1") must already have its L_D_COULEE row -
            // it is tracked elsewhere in AscoLSI before a P60 dispatch ever names it. A hot Coulee carries
            // no such precondition. Either way, one real Coulee routinely dispatches through several P60
            // Fichiers (several OF from the same cast) sharing the same IdCoulee, so an already-present
            // Coulee is never re-added - only a genuinely new one is (AD-7's "no navigation properties"
            // keeps this a plain existence check, not a relationship).
            string coulee = entity.Coulee;
            bool couleeAlreadyExists = context.CouleeRows.Any(row => row.IdCoulee == coulee);
            if (entity.CodeConsignePits == Kape22ImportBundle.ColdConsignePits && !couleeAlreadyExists)
            {
                string couleeMessage = $"OF '{of}' : la coulée '{coulee}' est introuvable dans L_D_COULEE.";
                ConversionError couleeError = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = couleeMessage };
                context.LogCommandeRows.Add(BuildLogRow(of, $"{numeroFichier} — REJETÉ : {couleeMessage}"));

                // A SaveChanges failure writing this REJETÉ row still needs to carry the missing-Coulee
                // reason forward, the same way PersistRejected's own catch forwards bundle.Errors.
                try
                {
                    context.SaveChanges();
                }
                catch (Exception exception) when (exception is DbUpdateException or DbException)
                {
                    return PersistenceFailure(exception, [couleeError]);
                }

                return new ImportResult { Errors = [couleeError] };
            }

            // A-5 (Epic 4 retro): a same-bundle L_D_CONSIGNES natural-key collision - two sections
            // sharing the same CodeOperation for this OF (today's TypeConsigne/ConsigneGPAO defaults,
            // 0/false, make CodeOperation the effective discriminant - see ConsignesMapper's own
            // comment) - would otherwise throw an uncaught InvalidOperationException at AddRange time.
            // Caught here, in-memory, before anything is staged on the context, and routed through the
            // same ConversionError/REJETÉ-log shape as the missing-Coulee check above - never widening
            // the DbUpdateException/DbException catch filter to cover it.
            // The full 4-part tuple mirrors L_D_CONSIGNES' real natural key, but OF is bundle-constant
            // today (every ConsignesMapper.Build call passes the same source.OF), so this grouping is
            // today effectively just CodeOperation, since TypeConsigne/ConsigneGPAO are always 0/false
            // (spec Design Notes).
            List<L_D_CONSIGNES>? collidingGroup = bundle.Consignes
                .GroupBy(row => (row.OF, row.CodeOperation, row.TypeConsigne, row.ConsigneGPAO))
                .FirstOrDefault(group => group.Count() > 1)
                ?.ToList();
            if (collidingGroup is not null)
            {
                string collisionMessage = $"OF '{of}' : plusieurs consignes partagent le même "
                    + $"CodeOperation '{collidingGroup[0].CodeOperation}'.";
                ConversionError collisionError = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = collisionMessage };
                context.LogCommandeRows.Add(BuildLogRow(of, $"{numeroFichier} — REJETÉ : {collisionMessage}"));

                try
                {
                    context.SaveChanges();
                }
                catch (Exception exception) when (exception is DbUpdateException or DbException)
                {
                    return PersistenceFailure(exception, [collisionError]);
                }

                return new ImportResult { Errors = [collisionError] };
            }

            context.Kape22Rows.Add(entity);
            context.LogCommandeRows.Add(BuildLogRow(of, numeroFichier + OkMessageSuffix));
            context.OrdreFabricationRows.Add(bundle.OrdreFabrication!);
            if (!couleeAlreadyExists)
            {
                context.CouleeRows.Add(bundle.Coulee!);
            }

            AddIfPresent(context.SectionChargeChutageRows, bundle.SectionChargeChutage);
            AddIfPresent(context.SectionChargeDecoupeRows, bundle.SectionChargeDecoupe);
            AddIfPresent(context.SectionChargeLingotRows, bundle.SectionChargeLingot);
            AddIfPresent(context.SectionChargePitsRows, bundle.SectionChargePits);
            AddIfPresent(context.SectionChargePoidsMetriqueRows, bundle.SectionChargePoidsMetrique);
            AddIfPresent(context.SectionChargeRefroidissoirsRows, bundle.SectionChargeRefroidissoirs);
            AddIfPresent(context.SectionChargeSvtRows, bundle.SectionChargeSvt);
            context.ConsignesRows.AddRange(bundle.Consignes);

            // AC-FR21-1/AD-1: a single SaveChanges wraps L_D_KAPE22 and every downstream entity - including
            // as many Consignes rows as the bundle carries - in one transaction, so either all of them land
            // or none does.
            context.SaveChanges();

            return new ImportResult
            {
                InsertedId = entity.Id,
                Warnings = bundle.Warnings,
            };
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            return PersistenceFailure(exception);
        }
    }

    private ImportResult PersistRejected(Kape22ImportBundle bundle)
    {
        // D15: with no readable OF nothing can be written to L_D_LOG_COMMANDE (only MQTTnetServices.Logs,
        // an Epic 3 concern). The rejection still comes back for the orchestrator to move the Fichier
        // to error/.
        if (string.IsNullOrWhiteSpace(bundle.OF))
        {
            return new ImportResult { Errors = bundle.Errors };
        }

        string numeroFichier = bundle.NumeroFichier ?? string.Empty;
        string message = $"{numeroFichier} — REJETÉ : {Summarize(bundle.Errors)}";

        try
        {
            // AC-FR11-4: a dedicated transaction for the single rejection log row.
            context.LogCommandeRows.Add(BuildLogRow(bundle.OF.Trim(), message));
            context.SaveChanges();

            return new ImportResult { Errors = bundle.Errors };
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // The rejection reasons ride along with the persistence failure so the orchestrator still
            // sees why the Fichier was rejected.
            return PersistenceFailure(exception, bundle.Errors);
        }
    }

    private bool OkLogRowExists(string numeroFichier, string of)
    {
        string okMessage = numeroFichier + OkMessageSuffix;
        return context.LogCommandeRows.Any(row => row.OF == of && row.Message == okMessage);
    }

    // Adds a Story 4.1 downstream entity only when the Story 4.3/4.4 mapper found its section applicable
    // to this OF (AC-FR21-1: "every non-null SectionCharge*").
    private static void AddIfPresent<TEntity>(DbSet<TEntity> rows, TEntity? entity)
        where TEntity : class
    {
        if (entity is not null)
        {
            rows.Add(entity);
        }
    }

    private L_D_LOG_COMMANDE BuildLogRow(string of, string message) => new()
    {
        Commande = configuration[CommandeKey] ?? DefaultCommande,
        Date = TimeZoneInfo.ConvertTimeFromUtc((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, ParisTime.Zone),
        Message = message,
        // P60 rows are not lingot-scoped; the NOT NULL column carries the neutral 0.
        NumLingot = 0,
        OF = of,
        // Trace = true keeps the row in the AscoLSI business-log views (Annexe C.2).
        Trace = true,
        User = this.user,
    };

    // AC-FR11-5/AC-FR21-2: the failed SaveChanges has already rolled back its transaction - none of the
    // staged rows persist. Report the SQL cause as a File-level PersistenceError; no
    // DbUpdateException or DbException leaves the persister. Any priorErrors (the reasons a rejected
    // Fichier was rejected) are kept ahead of it here. The caller Kape22FichierProcessor.Import then
    // re-sorts the ImportResult by LineNumber, which moves this File-level entry (LineNumber 0) to the
    // front (AC-FR6-4 extended to ImportResult).
    private static ImportResult PersistenceFailure(Exception exception, IReadOnlyList<ConversionError>? priorErrors = null)
    {
        string cause = (exception.InnerException ?? exception).Message;
        ConversionError persistenceError = new()
        {
            Block = Block.File,
            Code = ErrorCode.PersistenceError,
            Message = $"Échec de la persistance dans AscoLSI : {cause}",
        };

        return new ImportResult
        {
            Errors = priorErrors is { Count: > 0 } ? [.. priorErrors, persistenceError] : [persistenceError],
        };
    }

    // Import:InitiatingServer, length-checked against the L_D_LOG_COMMANDE.User column so a
    // misconfiguration fails at construction with a clear message instead of on every import.
    private static string ResolveUser(IConfiguration configuration)
    {
        string user = configuration[InitiatingServerKey] ?? Environment.MachineName;
        if (user.Length > LogCommandeColumnLengths.User)
        {
            throw new ArgumentException(
                $"'{InitiatingServerKey}' resolves to {user.Length} characters; L_D_LOG_COMMANDE.User holds {LogCommandeColumnLengths.User}.",
                nameof(configuration));
        }

        return user;
    }

    // The rejection summary carried in L_D_LOG_COMMANDE, shaped "<count> erreur(s) : <message> ; ...".
    private static string Summarize(IReadOnlyList<ConversionError> errors) =>
        $"{errors.Count} erreur(s) : {string.Join(" ; ", errors.Select(error => error.Message))}";
}
