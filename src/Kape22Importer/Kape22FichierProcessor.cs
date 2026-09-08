using System;
using System.Collections.Generic;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
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
// Story 3.3 adds the double journalisation (MQTTnetServices.Logs + L_D_LOG_COMMANDE) and the ILogger
// it needs.
public sealed class Kape22FichierProcessor(
    Func<AscoLsiDbContext> newContext,
    IConfiguration configuration,
    ImportOptions options,
    TimeProvider timeProvider)
    : IFichierProcessor
{
    // Runs the full per-Fichier pipeline and reports the step reached (AC-FR13-1). The bytes come from
    // IFileSource and the name from a directory listing; both are guarded here like Converter.Convert
    // and Kape22Persister.Persist guard their own inputs.
    public ImportResult Import(string fichierName, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(fichierName);
        ArgumentNullException.ThrowIfNull(content);

        // The first stage turns the raw bytes into the normalized XML. A failure here stops the pipeline
        // with no Xml, so the Fichier goes to error/ with only its .errors.json (AC-FR13-2).
        ConversionResult conversion = Converter.Convert(content, EmbeddedDescriptor.Xml);
        if (!conversion.Success)
        {
            return new ImportResult { Errors = conversion.Errors, Warnings = conversion.Warnings };
        }

        string normalizedXml = conversion.Xml!;

        // The next stage maps the normalized XML onto an L_D_KAPE22 entity. A mapping failure is not
        // short-circuited here: Kape22Persister.Persist is still called with the failed MapResult so it
        // writes the "<NumeroFichier> — REJETÉ" L_D_LOG_COMMANDE line when the OF is readable
        // (AC-FR11-4). The normalized XML rides along on the result so InboxScanner can drop <nom>.xml
        // next to the Fichier in error/ for diagnosis (AC-FR13-3).
        MapResult<L_D_KAPE22> map = new Kape22Mapper(timeProvider).Map(normalizedXml, fichierName);

        // The Step 1 Segment warnings ahead of the mapper's FR-10 coherence warnings, kept whichever
        // step the Fichier reaches. A rejected MapResult drops its Warnings on the way through
        // Kape22Persister, so they are taken from map here, not from the persister result. AC-FR6-4 (one
        // list sorted by LineNumber) is a separate reconciliation still open in the epics.
        IReadOnlyList<ConversionError> warnings = [.. conversion.Warnings, .. map.Warnings];

        // The final stage persists, over a context that belongs to this Fichier alone so its transaction
        // and EF change tracker never touch another Fichier's (AC-FR13-4).
        using AscoLsiDbContext context = newContext();
        ImportResult persisted = new Kape22Persister(context, configuration, timeProvider).Persist(map);

        if (!persisted.Success)
        {
            return persisted with { NormalizedXml = normalizedXml, Warnings = warnings };
        }

        // Success (a fresh insert or the anti-duplicate skip, AC-FR11-6): the Fichier and its normalized
        // XML are bound for archive/<yyyy>/<MM>/ (AC-FR13-5).
        return persisted with
        {
            NormalizedXml = normalizedXml,
            Warnings = warnings,
            XmlArchivePath = ArchivePath(fichierName),
        };
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

    // The path where InboxScanner will write the normalized XML next to the archived Fichier (D11).
    // Kept in step with InboxScanner.Archive; a shared helper is tracked in deferred-work.md.
    private string ArchivePath(string fichierName)
    {
        DateTimeOffset parisNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ParisTime.Zone);
        return $"{options.ArchiveFolder}/{parisNow.Year:D4}/{parisNow.Month:D2}/{fichierName}.xml";
    }
}
