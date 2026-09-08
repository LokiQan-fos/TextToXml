using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextToXml;

namespace Kape22Importer;

// Story 3.2 (FR-13): the real IFichierProcessor. For one Fichier it runs a strict order - raw bytes ->
// Converter.Convert -> (on success) capture the normalized XML -> Kape22Mapper.Map -> Kape22Persister
// .Persist over a fresh AscoLsiDbContext - and returns an ImportResult that reflects exactly the step
// reached (AC-FR13-1). Only a Converter failure short-circuits: no normalized XML, no MapResult, the
// persister is never constructed (AC-FR13-2). A Mapper failure still goes through the persister (which
// writes the REJETÉ log row for a readable OF) and keeps the normalized XML so it can ride to error/
// next to the Fichier (AC-FR13-3). Each call builds its own context through newContext, so one
// Fichier's transaction and EF change tracker never touch another's (AC-FR13-4).
// The IFichierProcessor.Process adapter narrows the ImportResult onto the Story 3.1
// FichierProcessingResult seam that InboxScanner consumes; callers that need InsertedId or
// XmlArchivePath (Story 3.3 double logging, Story 3.6 end-to-end) call Import directly.
// Story 3.3 (FR-14): after every Fichier - success or rejection - Import emits the MQTTnetServices.Logs
// line through ILogger (Journal), while Kape22Persister writes the L_D_LOG_COMMANDE line when the OF is
// readable. Import also re-sorts the concatenated Warnings by LineNumber (AC-FR6-4 extended to
// ImportResult).
public sealed class Kape22FichierProcessor(
    Func<AscoLsiDbContext> newContext,
    IConfiguration configuration,
    ImportOptions options,
    TimeProvider timeProvider,
    ILogger<Kape22FichierProcessor> logger)
    : IFichierProcessor
{
    // Runs the full per-Fichier pipeline and reports the step reached (AC-FR13-1). The bytes come from
    // IFileSource and the name from a directory listing; both are guarded here like Converter.Convert
    // and Kape22Persister.Persist guard their own inputs.
    public ImportResult Import(string fichierName, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(fichierName);
        ArgumentNullException.ThrowIfNull(content);

        long startedAt = timeProvider.GetTimestamp();

        // The first stage turns the raw bytes into the normalized XML. A failure here stops the pipeline
        // with no Xml, so the Fichier goes to error/ with only its .errors.json (AC-FR13-2). This is the
        // structural rejection with no readable OF: no L_D_LOG_COMMANDE row, only the Logs line (D15,
        // AC-FR14-3).
        ConversionResult conversion = Converter.Convert(content, EmbeddedDescriptor.Xml);
        if (!conversion.Success)
        {
            ImportResult failed = new()
            {
                Errors = SortedByLine(conversion.Errors),
                Warnings = SortedByLine(conversion.Warnings),
            };
            Journal(fichierName, failed, numeroFichier: null, of: null, lineCount: 0, timeProvider.GetElapsedTime(startedAt));
            return failed;
        }

        string normalizedXml = conversion.Xml!;

        // The next stage maps the normalized XML onto an L_D_KAPE22 entity. A mapping failure is not
        // short-circuited here: Kape22Persister.Persist is still called with the failed MapResult so it
        // writes the "<NumeroFichier> — REJETÉ" L_D_LOG_COMMANDE line when the OF is readable
        // (AC-FR11-4). The normalized XML rides along on the result so InboxScanner can drop <nom>.xml
        // next to the Fichier in error/ for diagnosis (AC-FR13-3).
        MapResult<L_D_KAPE22> map = new Kape22Mapper(timeProvider).Map(normalizedXml, fichierName);

        // The Step 1 Segment warnings and the mapper's FR-10 coherence warnings, kept whichever step the
        // Fichier reaches. A rejected MapResult drops its Warnings on the way through Kape22Persister, so
        // they are taken from map here, not from the persister result. The concatenation is re-sorted by
        // LineNumber so the two sources interleave correctly (AC-FR6-4 extended to ImportResult).
        IReadOnlyList<ConversionError> warnings = SortedByLine([.. conversion.Warnings, .. map.Warnings]);

        // The final stage persists, over a context that belongs to this Fichier alone so its transaction
        // and EF change tracker never touch another Fichier's (AC-FR13-4).
        using AscoLsiDbContext context = newContext();
        ImportResult persisted = new Kape22Persister(context, configuration, timeProvider).Persist(map);

        ImportResult result = persisted with
        {
            Errors = SortedByLine(persisted.Errors),
            NormalizedXml = normalizedXml,
            Warnings = warnings,
            XmlArchivePath = persisted.Success ? ArchivePath(fichierName) : null,
        };

        Journal(fichierName, result, map.NumeroFichier, map.OF, DetailLineCount(normalizedXml), timeProvider.GetElapsedTime(startedAt));
        return result;
    }

    // AC-FR13 seam: InboxScanner (Story 3.1) only needs the normalized XML, the Errors and the Warnings.
    public FichierProcessingResult Process(string fichierName, byte[] content)
    {
        ImportResult result = Import(fichierName, content);
        return new FichierProcessingResult
        {
            Errors = result.Errors,
            NormalizedXml = result.NormalizedXml,
            Warnings = result.Warnings,
        };
    }

    // FR-14: the MQTTnetServices.Logs half of the double logging, one line per Fichier, prefixed
    // "[Kape22Importer][<Event>] : ..." like the other portal workers. Best-effort (AC-FR14-7): a Logs
    // sink that is down must never stop the L_D_KAPE22 insert, which has already committed by the time
    // this runs, so every failure to log is swallowed.
    private void Journal(
        string fichierName,
        ImportResult result,
        string? numeroFichier,
        string? of,
        int lineCount,
        TimeSpan elapsed)
    {
        try
        {
            if (!result.Success)
            {
                // AC-FR14-2 / AC-FR14-3: rejection - list every Error. The L_D_LOG_COMMANDE "REJETÉ"
                // line (readable OF only) is Kape22Persister's; this is the always-written Logs line.
                logger.LogError(
                    "[Kape22Importer][ImportRejected] : {Fichier} — {ErrorCount} erreur(s) : {Errors}",
                    fichierName,
                    result.Errors.Count,
                    string.Join(" ; ", result.Errors.Select(error => error.Message)));
                return;
            }

            if (result.AlreadyImported)
            {
                // AC-FR11-6 (D22): the Fichier is fine, it was already imported by an earlier run that
                // crashed before the file move.
                logger.LogWarning(
                    "[Kape22Importer][AlreadyImported] : {Fichier} NumeroFichier={NumeroFichier} OF={OF} déjà importé, ignoré.",
                    fichierName,
                    numeroFichier,
                    of?.Trim());
                return;
            }

            // AC-FR14-1: the success line carries the file name, NumeroFichier, OF, InsertedId, the
            // Detail line count and the elapsed time.
            logger.LogInformation(
                "[Kape22Importer][ImportSucceeded] : {Fichier} NumeroFichier={NumeroFichier} OF={OF} InsertedId={InsertedId} Lignes={LineCount} Durée={ElapsedMs} ms",
                fichierName,
                numeroFichier,
                of?.Trim(),
                result.InsertedId,
                lineCount,
                (long)elapsed.TotalMilliseconds);

            // AC-FR14-8: a Fichier imported with coherence Warnings gets a second Warning line listing
            // them, on top of the success line.
            if (result.Warnings.Count > 0)
            {
                logger.LogWarning(
                    "[Kape22Importer][CoherenceWarnings] : {Fichier} — {WarningCount} warning(s) : {Warnings}",
                    fichierName,
                    result.Warnings.Count,
                    string.Join(" ; ", result.Warnings.Select(warning => $"{warning.Code} {warning.Message}")));
            }
        }
        catch (Exception exception)
        {
            // AC-FR14-7: the import itself has already succeeded; a logging failure is not its problem.
            Debug.WriteLine($"MQTTnetServices.Logs write failed for {fichierName}: {exception.Message}");
        }
    }

    // AC-FR6-4 extended to ImportResult: Errors and Warnings are each returned sorted by LineNumber
    // ascending (File-level entries, LineNumber 0, first), as independent lists. OrderBy is stable, so
    // entries sharing a LineNumber keep the order the pipeline produced them in.
    internal static IReadOnlyList<ConversionError> SortedByLine(IEnumerable<ConversionError> entries) =>
        [.. entries.OrderBy(entry => entry.LineNumber)];

    // The number of Detail Lignes in the normalized XML (the <message> sections), reported as "nb
    // Lignes" on the success Logs line (AC-FR14-1). Converter.Convert has already produced this XML on
    // the path that reaches here, so it parses; the null-conditional only covers a missing Root and
    // never turns a committed import into a crash on the Logs line.
    private static int DetailLineCount(string normalizedXml) =>
        XDocument.Parse(normalizedXml).Root?.Elements("message").Count() ?? 0;

    // The path where InboxScanner will write the normalized XML next to the archived Fichier (D11).
    // Kept in step with InboxScanner.Archive; a shared helper is tracked in deferred-work.md.
    private string ArchivePath(string fichierName)
    {
        DateTimeOffset parisNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ParisTime.Zone);
        return $"{options.ArchiveFolder}/{parisNow.Year:D4}/{parisNow.Month:D2}/{fichierName}.xml";
    }
}
