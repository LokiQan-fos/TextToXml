using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.5 / FR-15: the InboxScanner half of the worker loop robustness. The loop survives an
// unexpected per-Fichier exception (AC-FR15-1), a reception folder that is unreachable for a tick
// (AC-FR15-2) and a database that is unreachable (AC-FR15-3), never losing or duplicating a Fichier.
// All Category=Unit over the in-memory IFileSource and a fake IFichierProcessor: no database and no
// disk. The real Kape22FichierProcessor against an unreachable database (AC-FR15-3) and the recycle
// case (AC-FR15-4) need real I/O and live in WorkerLoopRobustnessIntegrationTests (AR-12). Written
// test-first (CC-1): each fact was seen red against the pre-Story-3.5 InboxScanner, which routed every
// persistence failure to error/ with no processing/ retry, let a folder-listing fault escape the tick,
// tagged an unexpected throw PersistenceError rather than UnexpectedFailure, and filed each sidecar
// after the move. Vocabulary follows the PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class WorkerLoopRobustnessTests
{
    // A fixed summer instant: Paris is UTC+2, so the archive date folder is 2026/09.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    private const string ArchiveDateFolder = "archive/2026/09";

    private static ImportOptions Options() => new()
    {
        ArchiveFolder = "archive",
        ErrorFolder = "error",
        InboxPath = "inbox",
        InitiatingServer = "AFS017",
        PollingInterval = TimeSpan.FromSeconds(30),
        ProcessingFolder = "processing",
        RetentionDays = 30,
    };

    private static InboxScanner Scanner(
        IFileSource fileSource,
        IFichierProcessor processor,
        RecordingLogger<InboxScanner>? logger = null) =>
        new(fileSource, processor, Options(), new FixedClock(Now), logger ?? new RecordingLogger<InboxScanner>());

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // One File-level PersistenceError, the shape Kape22Persister returns when a DbException is caught
    // (AC-FR11-5) and Kape22FichierProcessor forwards onto the FichierProcessingResult.
    private static FichierProcessingResult PersistenceFailure() => new()
    {
        Errors =
        [
            new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.PersistenceError,
                Message = "Échec de la persistance dans AscoLSI : le serveur est injoignable.",
            },
        ],
        NormalizedXml = "<file />",
    };

    // AC-FR15-1: an unexpected exception thrown by the processor on one Fichier is caught and logged at
    // Error, that Fichier is quarantined to error/ as an UnexpectedFailure (a code distinct from a
    // persistence failure, so IsPersistenceFailure never parks it in processing/ to retry forever), its
    // .errors.json names the exception type and text, and the loop carries on and archives the next
    // Fichier. Red before Story 3.5: the throw path then produced a File-level PersistenceError.
    [Fact]
    [Trait("AC", "FR15-1")]
    public void Tick_UnexpectedExceptionOnOneFichier_QuarantinesItAsUnexpectedFailure_ContinuesWithNext_AcFr15_1()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("poison"), Now.AddMinutes(-2));
        source.Add("", "P60_847_682_002", Bytes("clean"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((name, _) => name == "P60_847_682_001"
            ? throw new InvalidOperationException("boom")
            : new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger).RunTick();

        Assert.True(source.Exists("error", "P60_847_682_001"), "the failing Fichier must land in error/.");
        Assert.False(source.Exists("processing", "P60_847_682_001"), "it must not be parked in processing/ to retry.");
        using JsonDocument report = JsonDocument.Parse(source.Content("error", "P60_847_682_001.errors.json"));
        Assert.Equal(nameof(ErrorCode.UnexpectedFailure), report.RootElement[0].GetProperty("Code").GetString());
        Assert.Contains("InvalidOperationException", report.RootElement[0].GetProperty("Message").GetString()!);
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_002"), "the next Fichier must still be archived.");
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-2: the reception folder is unreachable for a tick. The scan throws, the exception is
    // caught, one Warning is logged, and no Fichier is lost - the inbox Fichier is still there and the
    // processor was never called.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheReceptionFolderIsUnreachable_LogsWarning_LosesNothing_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();
        source.ListingFault = new IOException("The reception folder is unreachable.");

        InboxScanner scanner = Scanner(source, processor, logger);
        Exception? escaped = Record.Exception(() => scanner.RunTick());

        Assert.Null(escaped);
        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"), "the Fichier must stay in the inbox.");
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-2: a locked-down ACL surfaces as UnauthorizedAccessException rather than IOException; it
    // is handled the same way - one Warning, nothing lost, no exception escaping.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheReceptionFolderDeniesAccess_DegradesToWarning_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();
        source.ListingFault = new UnauthorizedAccessException("Access to the reception folder is denied.");

        InboxScanner scanner = Scanner(source, processor, logger);
        Exception? escaped = Record.Exception(() => scanner.RunTick());

        Assert.Null(escaped);
        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // AC-FR15-2: a Fichier already stranded in processing/ is untouched by a tick that cannot list the
    // reception folder - not processed, not moved, not lost - and is picked up once the folder is back.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheReceptionFolderIsUnreachable_AStrandedProcessingFichierIsUntouched_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-5));
        FakeFichierProcessor processor = new((name, _) => new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" });
        InboxScanner scanner = Scanner(source, processor);

        source.ListingFault = new IOException("The reception folder is unreachable.");
        scanner.RunTick();

        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("processing", "P60_847_682_001"), "the stranded Fichier must stay in processing/.");

        source.ListingFault = null;
        scanner.RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
    }

    // AC-FR15-2: once the reception folder is reachable again, the next tick processes the Fichier that
    // was waiting - nothing was lost while the folder was down.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_AfterTheReceptionFolderRecovers_TheNextTickProcessesTheWaitingFichier_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((name, _) => new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" });
        InboxScanner scanner = Scanner(source, processor);

        source.ListingFault = new IOException("The reception folder is unreachable.");
        scanner.RunTick();
        source.ListingFault = null;
        scanner.RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
    }

    // AC-FR15-2: PurgeRetention runs on the same tick, right after RunTick, over the same reception
    // root. When the folder is unreachable it must degrade to a Warning too, not throw an IOException
    // out of the worker loop.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void TickThenPurge_WhenTheFolderIsUnreachable_BothDegradeToWarning_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("archive/2026/07", "old", Bytes("aged"), Now.AddDays(-90));
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();
        InboxScanner scanner = Scanner(source, processor, logger);
        source.ListingFault = new IOException("The reception folder is unreachable.");

        Exception? escaped = Record.Exception(() =>
        {
            scanner.RunTick();
            scanner.PurgeRetention();
        });

        Assert.Null(escaped);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.True(source.Exists("archive/2026/07", "old"), "the purge was skipped, so the aged file is still there.");
    }

    // AC-FR15-2: an I/O fault while filing one Fichier's outcome (the move into archive/ or error/
    // fails) is a per-Fichier problem, not a reason to abandon the tick. Each affected Fichier stays in
    // processing/ for a retry and the loop keeps going to the next one.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheOutcomeCannotBeMovedOutOfProcessing_LeavesEachForRetry_AndKeepsGoing_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("one"), Now.AddMinutes(-3));
        source.Add("processing", "P60_847_682_002", Bytes("two"), Now.AddMinutes(-2));
        FakeFichierProcessor processor = new((name, _) => new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" });
        RecordingLogger<InboxScanner> logger = new();
        source.MoveFault = new IOException("archive/ is unreachable.");

        Scanner(source, processor, logger).RunTick();

        Assert.Equal(["P60_847_682_001", "P60_847_682_002"], processor.Calls);
        Assert.True(source.Exists("processing", "P60_847_682_001"), "the Fichier must be left in processing/.");
        Assert.True(source.Exists("processing", "P60_847_682_002"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-2: the move of an inbox Fichier into processing/ fails this tick (processing/ unreachable,
    // an ACL race between the stability probe and the move). One Warning naming the Fichier and the
    // target, the tick is abandoned, and every inbox Fichier stays put - nothing is read or lost.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheMoveIntoProcessingFails_LeavesEveryInboxFichierPut_LogsWarning_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("one"), Now.AddMinutes(-2));
        source.Add("", "P60_847_682_002", Bytes("two"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();
        source.MoveFault = new IOException("processing/ is unreachable.");

        Exception? escaped = Record.Exception(() => Scanner(source, processor, logger).RunTick());

        Assert.Null(escaped);
        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"));
        Assert.True(source.Exists("", "P60_847_682_002"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-2: an I/O fault reading one Fichier from processing/ (a lock, a Fichier that vanished
    // between the listing and the read) leaves that Fichier in processing/ for the next tick and logs a
    // Warning, not an Error. The loop keeps going: a second unreadable Fichier gets its own Warning
    // rather than the tick unwinding at the first.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenReadingFichiersHitsAnIoFault_LeavesThemInProcessing_KeepsGoing_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("locked one"), Now.AddMinutes(-3));
        source.Add("processing", "P60_847_682_002", Bytes("locked two"), Now.AddMinutes(-2));
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();
        source.ReadFault = new IOException("The Fichier is locked by another process.");

        Exception? escaped = Record.Exception(() => Scanner(source, processor, logger).RunTick());

        Assert.Null(escaped);
        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("processing", "P60_847_682_001"));
        Assert.True(source.Exists("processing", "P60_847_682_002"));
        Assert.Equal(2, logger.AtLevel(LogLevel.Warning).Count);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-2 / AC-FR12-4: the <name>.errors.json sidecar of a rejected Fichier cannot be written.
    // The sidecar is written before the Fichier is moved, so the failure leaves the Fichier in
    // processing/ with nothing half-done - not stranded in error/ without its report - and the next
    // tick re-rejects it and writes the report properly. One Warning, no Error, no exception escaping.
    [Fact]
    [Trait("AC", "FR15-2")]
    public void Tick_WhenTheRejectionSidecarCannotBeWritten_LeavesTheFichierInProcessing_AcFr15_2()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        ConversionError rejection = new()
        {
            Block = Block.Header,
            Code = ErrorCode.RequiredFieldMissing,
            FieldId = "Client",
            LineNumber = 2,
            Message = "Le Champ obligatoire 'Client' de l'Entête est vide.",
        };
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { Errors = [rejection] });
        RecordingLogger<InboxScanner> logger = new();
        source.WriteFault = new IOException("error/ is read-only.");

        Exception? escaped = Record.Exception(() => Scanner(source, processor, logger).RunTick());

        Assert.Null(escaped);
        Assert.True(source.Exists("processing", "P60_847_682_001"), "a failed sidecar write must leave the Fichier in processing/.");
        Assert.False(source.Exists("error", "P60_847_682_001"), "the Fichier must not reach error/ without its report.");
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR15-3: a persistence failure (the database is unreachable) comes back from the processor as a
    // File-level PersistenceError. The Fichier is left in processing/ for a later retry - not moved to
    // error/, no <name>.errors.json - and one Warning is logged.
    [Fact]
    [Trait("AC", "FR15-3")]
    public void Tick_WhenPersistenceFails_LeavesFichierInProcessing_NotError_LogsWarning_AcFr15_3()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((_, _) => PersistenceFailure());
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger).RunTick();

        Assert.True(source.Exists("processing", "P60_847_682_001"), "the Fichier must be left in processing/ for a retry.");
        Assert.False(source.Exists("error", "P60_847_682_001"), "a persistence failure must not quarantine the Fichier.");
        Assert.False(source.Exists("error", "P60_847_682_001.errors.json"));
        Assert.False(source.Exists("", "P60_847_682_001"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // AC-FR15-3: a Fichier left in processing/ by a persistence failure is retried on the next tick
    // through the stranded-in-processing scan, and archived once the database is back.
    [Fact]
    [Trait("AC", "FR15-3")]
    public void Tick_FichierLeftInProcessingByAPersistenceFailure_IsRetriedAndArchivedNextTick_AcFr15_3()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        int calls = 0;
        FakeFichierProcessor processor = new((name, _) => ++calls == 1
            ? PersistenceFailure()
            : new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" });
        InboxScanner scanner = Scanner(source, processor);

        scanner.RunTick();
        scanner.RunTick();

        Assert.Equal(["P60_847_682_001", "P60_847_682_001"], processor.Calls);
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
        Assert.False(source.Exists("processing", "P60_847_682_001"));
    }

    // AC-FR15-3: a Fichier that is both malformed (a mapper rejection) and hit by a transient database
    // outage on the rejection-log write comes back carrying both the rejection reason and a File-level
    // PersistenceError. It is treated as a persistence failure - left in processing/, not quarantined -
    // so the retry can write the rejection log row and file it to error/ properly once the database is back.
    [Fact]
    [Trait("AC", "FR15-3")]
    public void Tick_WhenARejectedFichierAlsoHitsADatabaseOutage_LeavesItInProcessing_AcFr15_3()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FichierProcessingResult composite = new()
        {
            Errors =
            [
                new ConversionError
                {
                    Block = Block.Header,
                    Code = ErrorCode.RequiredFieldMissing,
                    FieldId = "Client",
                    LineNumber = 2,
                    Message = "Le Champ obligatoire 'Client' de l'Entête est vide.",
                },
                new ConversionError
                {
                    Block = Block.File,
                    Code = ErrorCode.PersistenceError,
                    Message = "Échec de la persistance dans AscoLSI : le serveur est injoignable.",
                },
            ],
            NormalizedXml = "<file />",
        };
        FakeFichierProcessor processor = new((_, _) => composite);
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger).RunTick();

        Assert.True(source.Exists("processing", "P60_847_682_001"), "the composite failure must be left in processing/.");
        Assert.False(source.Exists("error", "P60_847_682_001"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // AC-FR15-3: only a persistence failure (PersistenceError) leaves a Fichier in processing/ to retry.
    // A File-level SchemaInvalid result - the normalized XML failed P60.xsd, a permanent defect - is a
    // rejection: it must be quarantined to error/ with its .errors.json, never parked in processing/ to
    // loop forever. Guards IsPersistenceFailure against being widened to match SchemaInvalid.
    [Fact]
    [Trait("AC", "FR15-3")]
    public void Tick_WhenTheResultCarriesASchemaInvalidError_QuarantinesToError_NotProcessing_AcFr15_3()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FichierProcessingResult schemaInvalid = new()
        {
            Errors =
            [
                new ConversionError
                {
                    Block = Block.File,
                    Code = ErrorCode.SchemaInvalid,
                    Message = "Le XML normalisé n'est pas conforme à P60.xsd : élément 'Bogus' inattendu.",
                },
            ],
        };
        FakeFichierProcessor processor = new((_, _) => schemaInvalid);

        Scanner(source, processor).RunTick();

        Assert.True(source.Exists("error", "P60_847_682_001"), "a schema failure must be quarantined to error/.");
        Assert.True(source.Exists("error", "P60_847_682_001.errors.json"));
        Assert.False(source.Exists("processing", "P60_847_682_001"), "a schema failure must not be parked in processing/.");
    }
}
