using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Kape22Importer;

// FR-12: scans Import:InboxPath through IFileSource, moves each Fichier to processing/ before it is
// read, files successes under archive/<yyyy>/<MM>/ with their normalized XML alongside and rejects
// under error/ with a <name>.errors.json, and purges archive/ and error/ past Import:RetentionDays.
// The per-Fichier pipeline itself is IFichierProcessor (Story 3.2); the worker loop that calls RunTick
// on a PeriodicTimer is Story 3.4 / 3.5.
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

    // The derived archive date folder is read in Paris local time, like Kape22Mapper.DateReception.
    private static readonly TimeZoneInfo ParisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    // Sidecar files are UTF-8 without a BOM, like the XML preview harness.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly JsonSerializerOptions ErrorsReportJson = new() { WriteIndented = true };

    // Processes every Fichier stranded in processing/ by a killed worker (AC-FR12-6), then every stable
    // Fichier in the inbox, oldest first (AC-FR12-1), each moved to processing/ before it is read
    // (AC-FR12-2). An empty inbox is a no-op (AC-FR12-7).
    public void RunTick()
    {
        foreach (FichierEntry stranded in Ordered(fileSource.List(options.ProcessingFolder)))
        {
            ProcessFromProcessing(stranded.Name);
        }

        foreach (FichierEntry ready in Ordered(StableInboxFichiers()))
        {
            // AC-FR12-2: the Fichier is moved out of the inbox first; the processing/ copy is the one
            // that is read.
            fileSource.Move(InboxFolder, ready.Name, options.ProcessingFolder, ready.Name);
            ProcessFromProcessing(ready.Name);
        }
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

            foreach (FichierEntry entry in fileSource.ListRecursive(root).Where(entry => entry.LastWriteTimeUtc < cutoff))
            {
                fileSource.Delete(entry.Folder, entry.Name);
            }
        }
    }

    private void ProcessFromProcessing(string fichierName)
    {
        byte[] content = fileSource.Read(options.ProcessingFolder, fichierName);
        FichierProcessingResult result = processor.Process(fichierName, content);

        if (result.Success)
        {
            Archive(fichierName, result.NormalizedXml);
        }
        else
        {
            Reject(fichierName, result);
        }

        logger.LogInformation(
            "Fichier {Fichier} processed: {Outcome}.", fichierName, result.Success ? "archived" : "rejected");
    }

    // AC-FR12-3 (D11): the Fichier lands in archive/<yyyy>/<MM>/ and its normalized XML is written next
    // to it as <name>.xml.
    private void Archive(string fichierName, string? normalizedXml)
    {
        DateTimeOffset parisNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), ParisTimeZone);
        string dateFolder = $"{options.ArchiveFolder}/{parisNow.Year:D4}/{parisNow.Month:D2}";

        fileSource.Move(options.ProcessingFolder, fichierName, dateFolder, fichierName);
        if (normalizedXml is not null)
        {
            fileSource.Write(dateFolder, fichierName + NormalizedXmlExtension, Utf8NoBom.GetBytes(normalizedXml));
        }
    }

    // AC-FR12-4: the Fichier lands in error/ with a <name>.errors.json holding the Errors array. The
    // normalized XML rides along when the Converter produced one before the Mapper rejected the Fichier
    // (AC-FR13-3).
    private void Reject(string fichierName, FichierProcessingResult result)
    {
        fileSource.Move(options.ProcessingFolder, fichierName, options.ErrorFolder, fichierName);
        fileSource.Write(
            options.ErrorFolder,
            fichierName + ErrorsReportExtension,
            JsonSerializer.SerializeToUtf8Bytes(result.Errors, ErrorsReportJson));

        if (result.NormalizedXml is not null)
        {
            fileSource.Write(options.ErrorFolder, fichierName + NormalizedXmlExtension, Utf8NoBom.GetBytes(result.NormalizedXml));
        }
    }

    // AC-FR12-5: a Fichier whose reported size changes between two consecutive probes is still being
    // written; it is left in the inbox for a later tick, with nothing logged.
    private IEnumerable<FichierEntry> StableInboxFichiers()
    {
        List<FichierEntry> firstProbe = [.. fileSource.List(InboxFolder)];
        Dictionary<string, long> secondProbe = fileSource.List(InboxFolder).ToDictionary(entry => entry.Name, entry => entry.Length);

        return firstProbe.Where(entry =>
            secondProbe.TryGetValue(entry.Name, out long length) && length == entry.Length);
    }

    // AC-FR12-1: oldest to newest, a deterministic order (the Name breaks ties between equal timestamps).
    private static IEnumerable<FichierEntry> Ordered(IEnumerable<FichierEntry> entries) =>
        entries.OrderBy(entry => entry.LastWriteTimeUtc).ThenBy(entry => entry.Name, StringComparer.Ordinal);
}
