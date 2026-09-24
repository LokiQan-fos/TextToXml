using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
// ConsignesRows.AddRange is ever called (A-5, Epic 4 retro). A scaled decimal column whose value
// exceeds its DECIMAL(p,s) column's magnitude (DownstreamColumnMagnitudes) is rejected the same way
// too, before anything is staged (B-5, Story 4.10 hardening).
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

            // Business note (Q-5, Épic 4 retro #3): a Coulee is created once, by the first OF that
            // references it, and reused as-is by every later OF of the same Coulee - a later OF never
            // modifies the row a previous one created.
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
            // sharing the same CodeOperation for this OF - would otherwise throw an uncaught
            // InvalidOperationException at AddRange time. Caught here, in-memory, before anything is
            // staged on the context, and routed through the same ConversionError/REJETÉ-log shape as the
            // missing-Coulee check above - never widening the DbUpdateException/DbException catch filter
            // to cover it.
            // The full 4-part tuple mirrors L_D_CONSIGNES' real natural key. OF is bundle-constant today
            // (every ConsignesMapper.Row call passes the same source.OF) and ConsigneGPAO is always
            // true since Story 4.4-bis, but TypeConsigne now varies per row (0-29, one full-code row plus
            // one row per decoded sub-field per applicable section - see ConsignesMapper): every
            // decodable section's own TypeConsigne=13 full-code row still shares the same (OF,
            // CodeOperation, TypeConsigne, ConsigneGPAO) key whenever two sections share one
            // CodeOperation, so the collision this check exists for remains reachable through that row,
            // not through the whole tuple degenerating to CodeOperation alone. C-2 (Épic 4 retro #3):
            // CodeOperation collides case-insensitively at the real SQL Server (case-insensitive
            // collation) even though plain tuple equality would
            // treat "XC1" and "xc1" as distinct - ConsignesNaturalKeyComparer makes only CodeOperation
            // case-insensitive, OF/TypeConsigne/ConsigneGPAO stay ordinal, and the message below still
            // reads collidingGroup[0]'s own original-case CodeOperation, never a normalized form.
            List<L_D_CONSIGNES>? collidingGroup = bundle.Consignes
                .GroupBy(
                    row => (row.OF, row.CodeOperation, row.TypeConsigne, row.ConsigneGPAO),
                    ConsignesNaturalKeyComparer.Instance)
                .FirstOrDefault(group => group.Count() > 1)
                ?.ToList();

            // B-5 (Story 4.10 hardening): DecimalScale.Apply corrects decimal placement but never checks
            // a column's total DECIMAL(p,s) magnitude - an out-of-gabarit raw KAPE22 int, once scaled,
            // can still exceed it and would otherwise overflow at SaveChanges as an undiagnosed SQL
            // exception. Checked here, before anything is staged, against DownstreamColumnMagnitudes;
            // same ConversionError/REJETÉ-log shape as the check above.
            string? magnitudeOverflow = FindMagnitudeOverflow(bundle);

            // C-4 (Épic 4 retro #3): a mapped downstream string column whose value exceeds its
            // DownstreamColumnLengths bound would otherwise overflow at SaveChanges as an undiagnosed SQL
            // truncation error - the same posture B-5 already established for decimal magnitude. Checked
            // here, before anything is staged, against DownstreamColumnLengths.
            string? lengthOverflow = FindLengthOverflow(bundle);

            // C-6 (Épic 4 retro #3): A-5, B-5 and C-4 are independent pre-checks, but used to reject
            // sequentially - a bundle failing more than one only ever reported whichever fired first, one
            // SaveChanges each (a code-review finding: C-4 originally shipped after this accumulation as
            // its own separate early return, silently reintroducing the same "first cause wins" symptom
            // one guard later). All three now run unconditionally above and accumulate into a single
            // REJETÉ log row / ConversionError when more than one fires. The missing-Coulee check above
            // stays its own separate, untouched early return - out of this accumulation's scope.
            List<string> businessRuleMessages = [];
            if (collidingGroup is not null)
            {
                businessRuleMessages.Add("plusieurs consignes partagent le même "
                    + $"CodeOperation '{collidingGroup[0].CodeOperation}'.");
            }

            if (magnitudeOverflow is not null)
            {
                businessRuleMessages.Add($"la valeur convertie {magnitudeOverflow} dépasse le gabarit de sa colonne.");
            }

            if (lengthOverflow is not null)
            {
                businessRuleMessages.Add($"la valeur '{lengthOverflow}' dépasse la longueur maximale de sa colonne.");
            }

            if (businessRuleMessages.Count > 0)
            {
                return RejectWithBusinessRuleViolation(of, numeroFichier, businessRuleMessages);
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

            // C-7 (Épic 4 retro #3): Pits is never actually null here - AC-FR20-4 already rejected the
            // bundle upstream (Kape22ImportBundleMapper) when SectionChargePitsMapper returned null - but
            // AddIfPresent's own null-check still guards it uniformly with its 6 siblings.
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

    // Business note (Q-3, Épic 4 retro #3): an OF cannot be resubmitted today - a failed OF is reissued
    // under a new OF number rather than retried under the same NumeroFichier/OF pair.
    private bool OkLogRowExists(string numeroFichier, string of)
    {
        string okMessage = numeroFichier + OkMessageSuffix;
        return context.LogCommandeRows.Any(row => row.OF == of && row.Message == okMessage);
    }

    // Shared shape behind the Consignes-collision (A-5), magnitude-overflow (B-5) and length-overflow
    // (C-4) pre-checks: one BusinessRuleViolation ConversionError, one REJETÉ L_D_LOG_COMMANDE row,
    // committed in its own SaveChanges - a SaveChanges failure here still carries the original message(s)
    // forward, the same way the missing-Coulee block above (left untouched, AC-FR20-5) does inline. C-6
    // (Épic 4 retro #3): messages is a list, not a single string, so the A-5+B-5 caller can combine both
    // causes into the one ConversionError/REJETÉ row a simultaneous failure of both must produce.
    private ImportResult RejectWithBusinessRuleViolation(string of, string numeroFichier, IReadOnlyList<string> messages)
    {
        string message = $"OF '{of}' : " + string.Join(" ; ", messages);
        ConversionError error = new() { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, Message = message };
        context.LogCommandeRows.Add(BuildLogRow(of, $"{numeroFichier} — REJETÉ : {message}"));

        try
        {
            context.SaveChanges();
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            return PersistenceFailure(exception, [error]);
        }

        return new ImportResult { Errors = [error] };
    }

    // B-5: the first mapped downstream decimal property whose value's magnitude reaches or exceeds its
    // column's DownstreamColumnMagnitudes bound, formatted "<Entity>.<Property> = <value>", or null when
    // every scaled column is in gabarit. Reflection over the mapped entities' own properties, not the
    // AD-2 "zero reflection" mapper style - this is the persister's own one-shot check, run once per
    // Fichier, not a mapper.
    private static string? FindMagnitudeOverflow(Kape22ImportBundle bundle)
    {
        foreach (object entity in DownstreamDecimalEntities(bundle))
        {
            foreach (PropertyInfo property in entity.GetType().GetProperties())
            {
                if (!DownstreamColumnMagnitudes.MaxAbsoluteValues.TryGetValue(property.Name, out decimal bound))
                {
                    continue;
                }

                if (property.GetValue(entity) is decimal actual && Math.Abs(actual) >= bound)
                {
                    return $"{entity.GetType().Name}.{property.Name} = {actual.ToString(CultureInfo.InvariantCulture)}";
                }
            }
        }

        return null;
    }

    // The bundle's downstream entities that can carry a DownstreamColumnMagnitudes-registered column -
    // OrdreFabrication always (non-null on the success path this check runs on), the 4 SectionCharge*
    // tables only when the Story 4.4 per-OF applicability rule kept them.
    private static IEnumerable<object> DownstreamDecimalEntities(Kape22ImportBundle bundle)
    {
        yield return bundle.OrdreFabrication!;

        if (bundle.SectionChargeChutage is not null)
        {
            yield return bundle.SectionChargeChutage;
        }

        if (bundle.SectionChargeDecoupe is not null)
        {
            yield return bundle.SectionChargeDecoupe;
        }

        if (bundle.SectionChargeLingot is not null)
        {
            yield return bundle.SectionChargeLingot;
        }

        if (bundle.SectionChargePits is not null)
        {
            yield return bundle.SectionChargePits;
        }
    }

    // C-4 (Épic 4 retro #3): the first mapped downstream string property whose value exceeds its
    // column's DownstreamColumnLengths bound, formatted "<Entity>.<Property> = <value>", or null when
    // every bounded column is within length. Same reflection-walk shape as FindMagnitudeOverflow, run
    // once per Fichier, not a mapper.
    private static string? FindLengthOverflow(Kape22ImportBundle bundle)
    {
        foreach (object entity in DownstreamStringEntities(bundle))
        {
            foreach (PropertyInfo property in entity.GetType().GetProperties())
            {
                if (!DownstreamColumnLengths.MaxLengths.TryGetValue(property.Name, out int maxLength))
                {
                    continue;
                }

                if (property.GetValue(entity) is string actual && actual.Length > maxLength)
                {
                    return $"{entity.GetType().Name}.{property.Name} = {actual}";
                }
            }
        }

        return null;
    }

    // C-4: the bundle's downstream entities that can carry a DownstreamColumnLengths-registered string
    // column - all 10 Story 4.1 tables (string columns exist outside FindMagnitudeOverflow's 5-table
    // decimal subset above), not DownstreamDecimalEntities reused as-is. OrdreFabrication and Coulee
    // always (non-null on the success path this check runs on), the 7 SectionCharge* tables only when
    // the Story 4.4 per-OF applicability rule kept them, and every Consignes row the bundle carries.
    private static IEnumerable<object> DownstreamStringEntities(Kape22ImportBundle bundle)
    {
        yield return bundle.OrdreFabrication!;
        yield return bundle.Coulee!;

        if (bundle.SectionChargeChutage is not null)
        {
            yield return bundle.SectionChargeChutage;
        }

        if (bundle.SectionChargeDecoupe is not null)
        {
            yield return bundle.SectionChargeDecoupe;
        }

        if (bundle.SectionChargeLingot is not null)
        {
            yield return bundle.SectionChargeLingot;
        }

        if (bundle.SectionChargePits is not null)
        {
            yield return bundle.SectionChargePits;
        }

        if (bundle.SectionChargePoidsMetrique is not null)
        {
            yield return bundle.SectionChargePoidsMetrique;
        }

        if (bundle.SectionChargeRefroidissoirs is not null)
        {
            yield return bundle.SectionChargeRefroidissoirs;
        }

        if (bundle.SectionChargeSvt is not null)
        {
            yield return bundle.SectionChargeSvt;
        }

        foreach (L_D_CONSIGNES consigne in bundle.Consignes)
        {
            yield return consigne;
        }
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
    // misconfiguration fails at construction with a clear message instead of on every import. Falls back
    // to the machine name on a blank/whitespace-only setting too, not just an absent one (GpaoImportP60.json
    // ships with "InitiatingServer": "" - an empty string, not null - which a bare `??` never catches,
    // silently leaving L_D_LOG_COMMANDE.User blank on every row).
    private static string ResolveUser(IConfiguration configuration)
    {
        string? configured = configuration[InitiatingServerKey];
        string user = string.IsNullOrWhiteSpace(configured) ? Environment.MachineName : configured;
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
