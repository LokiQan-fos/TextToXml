using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Schema;
using FichierJournal;

namespace P89Converter;

// One tick over the P89 source folder (Story 5.1, FR-22). Each LP89_* Fichier is converted by
// P89FichierConverter, then, in this order: its XML is written under <name>_<yyyyMMddHHmmss>.xml, one
// IFichierJournal entry is recorded (success or failure; whether a row is written is the journal's
// concern, D15), and the Fichier is moved to the done folder, or to the error folder when it failed, under
// the same suffix. The suffix comes from one clock reading per tick, so the index rotation 999 -> 001
// never collides with an earlier conversion, and nothing is ever overwritten. A done or error target that
// already exists defers the Fichier before anything is written or recorded. A journal failure or a
// file-system fault defers it too: the XML this tick wrote is deleted, the Fichier stays in the source
// folder and is retried at the next tick.
public sealed class P89FolderConverter
{
    private const string Commande = "P89";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IFichierJournal journal;
    private readonly P89Options options;
    private readonly XmlSchemaSet? schemas;
    private readonly TimeProvider timeProvider;

    public P89FolderConverter(P89Options options, IFichierJournal journal, TimeProvider timeProvider)
        : this(options, journal, timeProvider, null)
    {
    }

    // The schema is a seam so a test can stand in for a schema error no raw Fichier can produce.
    internal P89FolderConverter(
        P89Options options, IFichierJournal journal, TimeProvider timeProvider, XmlSchemaSet? schemas)
    {
        this.journal = journal;
        this.options = options;
        this.schemas = schemas;
        this.timeProvider = timeProvider;
    }

    // Processes every LP89_* Fichier of the source folder, in name order. The token is checked before each
    // Fichier, never inside one, so a Launcher stop lets the Fichier in flight finish (NFR-9).
    public IReadOnlyList<P89FichierOutcome> RunTick(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(this.options.DonePath);
        Directory.CreateDirectory(this.options.ErrorPath);
        Directory.CreateDirectory(this.options.XmlPath);

        // One clock reading: the file suffix (host local time) and the entry Instant derive from the same
        // instant.
        DateTimeOffset now = this.timeProvider.GetUtcNow();
        string suffix = TimeZoneInfo.ConvertTime(now, this.timeProvider.LocalTimeZone)
            .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        string[] paths = Directory.GetFiles(this.options.SourcePath, "LP89_*");
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

        List<P89FichierOutcome> outcomes = [];
        foreach (string path in paths)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // AC-FR22-9: a Fichier written more recently than the quiet period (or, through clock skew,
            // after now) may still be copied in; it is left in the source folder, with no outcome, and
            // retried at the next tick.
            if (now - File.GetLastWriteTimeUtc(path) < this.options.StabilityQuietPeriod)
            {
                continue;
            }

            outcomes.Add(this.Process(path, suffix, now));
        }

        return outcomes;
    }

    private static FichierJournalEntry Entry(string fichierName, P89Conversion conversion, DateTimeOffset now) => new()
    {
        Commande = Commande,
        FichierName = fichierName,
        Instant = now,
        NumeroFichier = conversion.NumeroFichier,
        OF = conversion.OF,
        Reasons = conversion.Reasons,
    };

    private static P89FichierOutcome Outcome(
        string fichierName, P89FichierStatus status, string targetName, IReadOnlyList<string> reasons) => new()
        {
            FichierName = fichierName,
            Reasons = reasons,
            Status = status,
            TargetName = targetName,
        };

    private static string TargetExists(string targetPath) => $"Fichier : {targetPath} existe déjà";

    // Best-effort cleanup: a failed delete (locked XML) must not replace the reason the Fichier is deferred
    // for. The XML is then left behind; the retry writes its own under a new suffix.
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // Success: XML, then the entry, then the move to done. A done target that already exists defers the
    // Fichier before anything is written, so a retry never records a second entry for it. Once this tick
    // has created the XML, any later failure (write, entry, move) deletes it so the retried Fichier does
    // not leave a second one behind. CreateNew stays outside that cleanup: an XML that already existed is
    // never deleted.
    private P89FichierOutcome Accept(string path, string targetName, P89Conversion conversion, DateTimeOffset now)
    {
        string fichierName = Path.GetFileName(path);
        string donePath = Path.Combine(this.options.DonePath, targetName);
        if (File.Exists(donePath))
        {
            return Outcome(fichierName, P89FichierStatus.Deferred, targetName, [TargetExists(donePath)]);
        }

        string xmlPath = Path.Combine(this.options.XmlPath, targetName + ".xml");
        FileStream stream = new(xmlPath, FileMode.CreateNew);
        try
        {
            using (StreamWriter writer = new(stream, Utf8))
            {
                writer.Write(conversion.Xml);
            }

            string? failure = this.TryRecord(Entry(fichierName, conversion, now));
            if (failure is not null)
            {
                TryDelete(xmlPath);
                return Outcome(fichierName, P89FichierStatus.Deferred, targetName, [failure]);
            }

            File.Move(path, donePath);
            return Outcome(fichierName, P89FichierStatus.Converted, targetName, []);
        }
        catch
        {
            // Process turns the rethrown file-system fault into a Deferred outcome.
            stream.Dispose();
            TryDelete(xmlPath);
            throw;
        }
    }

    private P89FichierOutcome Process(string path, string suffix, DateTimeOffset now)
    {
        string fichierName = Path.GetFileName(path);
        string targetName = $"{fichierName}_{suffix}";

        try
        {
            byte[] content = File.ReadAllBytes(path);
            P89Conversion conversion = this.schemas is null
                ? P89FichierConverter.Convert(content)
                : P89FichierConverter.Convert(content, this.schemas);

            return conversion.Success
                ? this.Accept(path, targetName, conversion, now)
                : this.Reject(path, targetName, conversion, now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Outcome(fichierName, P89FichierStatus.Deferred, targetName, [$"Fichier : {exception.Message}"]);
        }
    }

    // Failure: no XML; the entry with its reasons, then the move to error. An error target that already
    // exists defers the Fichier before its entry, as on the success path.
    private P89FichierOutcome Reject(string path, string targetName, P89Conversion conversion, DateTimeOffset now)
    {
        string fichierName = Path.GetFileName(path);
        string errorPath = Path.Combine(this.options.ErrorPath, targetName);
        if (File.Exists(errorPath))
        {
            return Outcome(fichierName, P89FichierStatus.Deferred, targetName, [TargetExists(errorPath)]);
        }

        string? failure = this.TryRecord(Entry(fichierName, conversion, now));
        if (failure is not null)
        {
            return Outcome(fichierName, P89FichierStatus.Deferred, targetName, [.. conversion.Reasons, failure]);
        }

        File.Move(path, errorPath);
        return Outcome(fichierName, P89FichierStatus.Rejected, targetName, conversion.Reasons);
    }

    // Records one entry; returns the failure reason, or null on success. Record throws on failure, and any
    // exception is caught (an unreachable server can surface as more than one type): an escaping exception
    // would abort the rest of the tick and leave the just-written XML behind. The Fichier is deferred.
    private string? TryRecord(FichierJournalEntry entry)
    {
        try
        {
            this.journal.Record(entry);
            return null;
        }
        catch (Exception exception)
        {
            return $"Journal : {(exception.InnerException ?? exception).Message}";
        }
    }
}

// The outcome of one Fichier in a tick, for the worker to log. Properties are declared in alphabetical
// order (CC-4).
public sealed class P89FichierOutcome
{
    public string FichierName { get; init; } = string.Empty;

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public P89FichierStatus Status { get; init; }

    // The done or error name <name>_<yyyyMMddHHmmss>, also the XML name without its .xml extension.
    public string TargetName { get; init; } = string.Empty;
}

// Where a Fichier ended up after a tick. Members are declared in alphabetical order (CC-4).
public enum P89FichierStatus
{
    // Written to the xml folder, recorded in the journal and moved to the done folder.
    Converted,

    // Left in the source folder, retried at the next tick (journal failure, file-system fault).
    Deferred,

    // Recorded in the journal with its reasons and moved to the error folder.
    Rejected,
}
