using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using TextToXml;

namespace Kape22Importer;

// FR-12: scans Import:InboxPath through IFileSource, moves each Fichier to processing/ before it is
// read, files successes under archive/<yyyy>/<MM>/ with their normalized XML alongside and rejects
// under error/ with a <name>.errors.json, and purges archive/ and error/ past Import:RetentionDays.
// The per-Fichier pipeline itself is IFichierProcessor (Story 3.2); the worker that calls RunTick on a
// timer is GpaoImportP60.Client in MicroServices.sln (Story 3.4).
public sealed class InboxScanner(
    IFileSource fileSource,
    IFichierProcessor processor,
    ImportOptions options,
    TimeProvider timeProvider,
    ILogger<InboxScanner> logger)
{
    // The inbox root folder, relative to the reception root.
    private const string InboxFolder = "";

    // The normalized XML kept next to an archived or rejected Fichier (D11).
    private const string NormalizedXmlExtension = ".xml";

    // The rejection report kept next to a Fichier in error/ (AC-FR12-4).
    private const string ErrorsReportExtension = ".errors.json";

    // Sidecar files are UTF-8 without a BOM, like the XML preview harness.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    // The rejection report serializes ErrorCode by name, not by ordinal, so it stays readable and
    // survives an enum member being inserted rather than appended.
    private static readonly JsonSerializerOptions ErrorsReportJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    // Processes every Fichier stranded in processing/ by a killed worker (AC-FR12-6), then every stable
    // Fichier in the inbox, oldest first (AC-FR12-1), each moved to processing/ before it is read
    // (AC-FR12-2). An empty inbox is a no-op (AC-FR12-7). A cancelled token stops the tick between two
    // Fichiers, never mid-Fichier, so a Launcher Stop unwinds within the shutdown budget and never
    // leaves a Fichier half-inserted (AC-FR14-6); the token is never passed into processor.Process.
    // AC-FR15-2: a reception folder that cannot be listed or moved into for this tick (a dead share, a
    // locked-down ACL) is logged once at Warning and the tick is abandoned - nothing is lost, the next
    // tick retries. A transient I/O fault touching one Fichier is contained inside ProcessFromProcessing
    // and never abandons the tick.
    public void RunTick(CancellationToken cancellationToken = default)
    {
        if (!TryList(options.ProcessingFolder, out IReadOnlyList<FichierEntry> stranded))
        {
            return;
        }

        foreach (FichierEntry entry in Ordered(stranded))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ProcessFromProcessing(entry.Name);
        }

        if (!TryStableInboxFichiers(out IReadOnlyList<FichierEntry> ready))
        {
            return;
        }

        foreach (FichierEntry entry in Ordered(ready))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // AC-FR12-2: the Fichier is moved out of the inbox first; the processing/ copy is the one
            // that is read.
            try
            {
                fileSource.Move(InboxFolder, entry.Name, options.ProcessingFolder, entry.Name);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    "Fichier {Fichier} could not be moved into '{Folder}' this tick ({Reason}); tick abandoned, will retry.",
                    entry.Name,
                    options.ProcessingFolder,
                    exception.Message);
                return;
            }

            ProcessFromProcessing(entry.Name);
        }
    }

    // Lists a reception folder, translating an unreachable folder (AC-FR15-2) into a single Warning and
    // a false return so the caller abandons the tick without losing anything.
    private bool TryList(string folder, out IReadOnlyList<FichierEntry> entries)
    {
        try
        {
            entries = fileSource.List(folder);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            entries = [];
            logger.LogWarning(
                "Reception folder '{Folder}' could not be listed this tick ({Reason}); skipped, will retry.",
                folder.Length == 0 ? "inbox" : folder,
                exception.Message);
            return false;
        }
    }

    // AC-FR12-5: a Fichier whose reported size changes between two consecutive probes is still being
    // written; it is left in the inbox for a later tick, with nothing logged. AC-FR15-2: an inbox that
    // cannot be listed is one Warning and a false return, so the tick is abandoned cleanly.
    private bool TryStableInboxFichiers(out IReadOnlyList<FichierEntry> stable)
    {
        stable = [];
        if (!TryList(InboxFolder, out IReadOnlyList<FichierEntry> firstProbe)
            || !TryList(InboxFolder, out IReadOnlyList<FichierEntry> secondProbe))
        {
            return false;
        }

        Dictionary<string, long> secondByName = secondProbe.ToDictionary(entry => entry.Name, entry => entry.Length);
        stable = [.. firstProbe.Where(entry =>
            secondByName.TryGetValue(entry.Name, out long length) && length == entry.Length)];
        return true;
    }

    // Deletes files in ArchiveFolder / ErrorFolder older than Import:RetentionDays; disabled when
    // RetentionDays is zero or less (AC-FR12-9, D13).
    public void PurgeRetention()
    {
        if (options.RetentionDays <= 0)
        {
            return;
        }

        DateTimeOffset cutoff = timeProvider.GetUtcNow().AddDays(-options.RetentionDays);
        foreach (string root in new[] { options.ArchiveFolder, options.ErrorFolder })
        {
            // An unset ArchiveFolder / ErrorFolder would make ListRecursive walk the whole reception
            // root and delete aged inbox / processing Fichiers; skip it.
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            try
            {
                foreach (FichierEntry entry in fileSource.ListRecursive(root).Where(entry => entry.LastWriteTimeUtc < cutoff))
                {
                    fileSource.Delete(entry.Folder, entry.Name);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // AC-FR15-2: the same unreachable share RunTick already skipped this tick. The purge is
                // best-effort - a missed sweep only means aged files linger one more tick - so it is
                // abandoned with a Warning rather than thrown out of the worker loop.
                logger.LogWarning(
                    "Retention purge of {Root} skipped, folder unreachable ({Reason}); will retry next tick.",
                    root,
                    exception.Message);
            }
        }
    }

    private void ProcessFromProcessing(string fichierName)
    {
        // The read is guarded on its own: a transient I/O fault reading this one Fichier (a file lock, a
        // Fichier removed between the listing and the read, a full disk) leaves it in processing/ for the
        // next tick and the loop moves straight on to the next Fichier. Only IOException and
        // UnauthorizedAccessException count as transient here; anything the read throws otherwise falls
        // through to the unexpected-failure path below.
        byte[] content;
        try
        {
            content = fileSource.Read(options.ProcessingFolder, fichierName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Fichier {Fichier} could not be read ({Reason}); left in processing/, will retry.",
                fichierName,
                exception.Message);
            return;
        }

        FichierProcessingResult result;
        try
        {
            result = processor.Process(fichierName, content);
        }
        catch (Exception exception)
        {
            // AC-FR13-4 / AC-FR15-1: one Fichier's unexpected throw (a deployment fault surfacing from
            // the mapper, an EF error that escapes Kape22Persister, a bug - an IOException raised inside
            // the pipeline included, since that is a defect, not the transient read fault handled above)
            // must not unwind the tick and strand every later Fichier. It is logged at Error and, as an
            // UnexpectedFailure result, quarantined in error/ by the shared outcome path below - a code
            // distinct from a persistence failure, so it is never mistaken for one and left to retry
            // forever.
            logger.LogError(
                exception, "Fichier {Fichier} threw during processing; quarantining to error/.", fichierName);
            result = UnexpectedFailure(exception);
        }

        // AC-FR15-3: the processor reported that AscoLSI is unreachable - Kape22Persister caught a
        // DbException, not anything wrong with the Fichier. The Fichier is left in processing/ for a
        // later retry (the next tick's stranded scan picks it up), never moved to error/, and the outage
        // is logged at Warning.
        if (IsPersistenceFailure(result))
        {
            logger.LogWarning(
                "Fichier {Fichier} left in processing/ after a persistence failure; will retry.", fichierName);
            return;
        }

        try
        {
            if (result.Success)
            {
                Archive(fichierName, result.NormalizedXml);
            }
            else
            {
                Reject(fichierName, result);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The outcome could not be filed - a sidecar write failed, or the Fichier could not be moved
            // out of processing/ because archive/ or error/ is unreachable or the Fichier is locked.
            // Both sidecars are written before the Fichier is moved, so the Fichier is still in
            // processing/ here with nothing half-done: the next tick re-runs the whole outcome, a
            // committed success is recognised by the D22 guard so no second row is inserted, and a
            // rejection is re-evaluated. Any sidecar already written is overwritten on the retry.
            logger.LogWarning(
                "Fichier {Fichier} outcome could not be filed ({Reason}); left in processing/ for retry.",
                fichierName,
                exception.Message);
            return;
        }

        logger.LogInformation(
            "Fichier {Fichier} processed: {Outcome}.", fichierName, result.Success ? "archived" : "rejected");
    }

    // The result stand-in for a Fichier whose processing threw: a single File-level UnexpectedFailure
    // error so the shared outcome path quarantines it to error/ with a readable .errors.json. The
    // Message carries the exception type and text (PRD Annexe D).
    private static FichierProcessingResult UnexpectedFailure(Exception exception) => new()
    {
        Errors =
        [
            new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.UnexpectedFailure,
                Message = $"Traitement interrompu par une erreur inattendue : {exception.GetType().Name} : {exception.Message}",
            },
        ],
    };

    // AC-FR12-3 (D11): the Fichier lands in archive/<yyyy>/<MM>/ and its normalized XML is written next
    // to it as <name>.xml. The sidecar is written first: if that write fails the Fichier is still in
    // processing/, so the caller's catch leaves it there for a clean retry rather than archiving it
    // without its XML.
    private void Archive(string fichierName, string? normalizedXml)
    {
        DateTimeOffset parisNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ParisTime.Zone);
        string dateFolder = $"{options.ArchiveFolder}/{parisNow.Year:D4}/{parisNow.Month:D2}";

        if (normalizedXml is not null)
        {
            fileSource.Write(dateFolder, fichierName + NormalizedXmlExtension, Utf8NoBom.GetBytes(normalizedXml));
        }

        fileSource.Move(options.ProcessingFolder, fichierName, dateFolder, fichierName);
    }

    // AC-FR12-4: the Fichier lands in error/ with a <name>.errors.json holding the Errors array. The
    // normalized XML rides along when the Converter produced one before the Mapper rejected the Fichier
    // (AC-FR13-3). Both sidecars are written before the Fichier is moved: a failed write leaves the
    // Fichier in processing/ (the caller's catch), so it is never stranded in error/ without its report.
    private void Reject(string fichierName, FichierProcessingResult result)
    {
        fileSource.Write(
            options.ErrorFolder,
            fichierName + ErrorsReportExtension,
            JsonSerializer.SerializeToUtf8Bytes(result.Errors, ErrorsReportJson));

        if (result.NormalizedXml is not null)
        {
            fileSource.Write(options.ErrorFolder, fichierName + NormalizedXmlExtension, Utf8NoBom.GetBytes(result.NormalizedXml));
        }

        fileSource.Move(options.ProcessingFolder, fichierName, options.ErrorFolder, fichierName);
    }

    // AC-FR15-3: a returned result carrying a File-level PersistenceError is Kape22Persister reporting a
    // caught DbException (AC-FR11-5), or Kape22FichierProcessor reporting one from its reference-data read
    // through the same helper (Story 4.12) - AscoLSI is unreachable. Only that helper emits PersistenceError
    // (a P60Deserializer schema failure is SchemaInvalid, an unexpected throw is UnexpectedFailure), so
    // it is a precise signal for "leave the Fichier in processing/ and retry", distinct from a Converter,
    // schema or Mapper rejection (which belongs in error/).
    private static bool IsPersistenceFailure(FichierProcessingResult result) =>
        result.Errors.Any(error => error is { Block: Block.File, Code: ErrorCode.PersistenceError });

    // AC-FR12-1: oldest to newest, a deterministic order (the Name breaks ties between equal timestamps).
    private static IEnumerable<FichierEntry> Ordered(IEnumerable<FichierEntry> entries) =>
        entries.OrderBy(entry => entry.LastWriteTimeUtc).ThenBy(entry => entry.Name, StringComparer.Ordinal);
}
