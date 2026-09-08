using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;

namespace Kape22Importer.Persistence;

// Story 2.8 (FR-11): turns a Kape22Mapper.MapResult into rows in AscoLSI.
// On a mapped Fichier: the anti-duplicate guard (D22) checks for an existing "<NumeroFichier> — OK"
// L_D_LOG_COMMANDE row for the same NumeroFichier + OF; if none, the L_D_KAPE22 insert and the "— OK"
// L_D_LOG_COMMANDE insert are committed together (AC-FR11-1, AC-FR11-3), and InsertedId is the identity
// value. If the guard finds a prior success, nothing is inserted and the Fichier comes back as an
// already-imported skip: Success true, InsertedId null, empty Errors (AC-FR11-6).
// On a rejected Fichier: no L_D_KAPE22 row; a single "<NumeroFichier> — REJETÉ : <summary>"
// L_D_LOG_COMMANDE row in its own transaction when the OF is readable (AC-FR11-4), nothing at all when
// it is not (D15).
// A SQL failure (DbUpdateException or DbException) is caught and returned as a PersistenceError; the
// failed SaveChanges has already rolled back its transaction (AC-FR11-5). Other exception types are not
// swallowed. The connection string reaches the DbContext through configuration and is never hard-coded
// (AC-FR11-8, CC-7).
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

    public ImportResult Persist(MapResult<L_D_KAPE22> mapResult)
    {
        ArgumentNullException.ThrowIfNull(mapResult);

        return mapResult.Success
            ? PersistMapped(mapResult)
            : PersistRejected(mapResult);
    }

    private ImportResult PersistMapped(MapResult<L_D_KAPE22> mapResult)
    {
        L_D_KAPE22 entity = mapResult.Value!;

        // Map always populates both fields on a deserialized Fichier, and this path is success-only.
        string numeroFichier = mapResult.NumeroFichier!;
        string of = mapResult.OF!;

        try
        {
            // AC-FR11-6/11-7 (D22): a committed "<NumeroFichier> — OK" row for this NumeroFichier + OF
            // means the Fichier already imported (a crash between the commit and the file move). Skip
            // it: no new row, InsertedId stays null, Success stays true so the orchestrator archives
            // the Fichier and logs the "deja importe" warning.
            if (OkLogRowExists(numeroFichier, of))
            {
                return new ImportResult { Warnings = mapResult.Warnings };
            }

            context.Kape22Rows.Add(entity);
            context.LogCommandeRows.Add(BuildLogRow(of, numeroFichier + OkMessageSuffix));

            // AC-FR11-3: a single SaveChanges wraps both inserts in one transaction, so either both
            // rows land or neither does.
            context.SaveChanges();

            return new ImportResult
            {
                InsertedId = entity.Id,
                Warnings = mapResult.Warnings,
            };
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            return PersistenceFailure(exception);
        }
    }

    private ImportResult PersistRejected(MapResult<L_D_KAPE22> mapResult)
    {
        // D15: with no readable OF nothing can be written to L_D_LOG_COMMANDE (only MQTTnetServices.Logs,
        // an Epic 3 concern). The rejection still comes back for the orchestrator to move the Fichier
        // to error/.
        if (string.IsNullOrWhiteSpace(mapResult.OF))
        {
            return new ImportResult { Errors = mapResult.Errors };
        }

        string numeroFichier = mapResult.NumeroFichier ?? string.Empty;
        string message = $"{numeroFichier} — REJETÉ : {Summarize(mapResult.Errors)}";

        try
        {
            // AC-FR11-4: a dedicated transaction for the single rejection log row.
            context.LogCommandeRows.Add(BuildLogRow(mapResult.OF.Trim(), message));
            context.SaveChanges();

            return new ImportResult { Errors = mapResult.Errors };
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            // The rejection reasons ride along with the persistence failure so the orchestrator still
            // sees why the Fichier was rejected.
            return PersistenceFailure(exception, mapResult.Errors);
        }
    }

    private bool OkLogRowExists(string numeroFichier, string of)
    {
        string okMessage = numeroFichier + OkMessageSuffix;
        return context.LogCommandeRows.Any(row => row.OF == of && row.Message == okMessage);
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

    // AC-FR11-5: the failed SaveChanges has already rolled back its transaction. Report the SQL cause as
    // a File-level PersistenceError; no DbUpdateException or DbException leaves the persister. Any
    // priorErrors (the reasons a rejected Fichier was rejected) are kept ahead of it.
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
