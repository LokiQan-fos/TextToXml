using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 3.1 / FR-12: InboxScanner scans the reception folder through IFileSource, moves each Fichier to
// processing/ before reading it, archives successes with their normalized XML and rejects failures with
// a .errors.json, leaves half-written Fichiers alone, resumes Fichiers stranded in processing/, and
// purges archive/ and error/ past the retention window. All AC-FR12 tests run over the in-memory
// IFileSource (no database, no disk). Written test-first (CC-1): each fact was seen red against a
// stubbed InboxScanner before the scan logic was implemented.
[Trait("Category", TestCategory.Unit)]
public class InboxScannerTests
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
        ImportOptions? options = null,
        RecordingLogger<InboxScanner>? logger = null) =>
        new(fileSource, processor, options ?? Options(), new FixedClock(Now), logger ?? new RecordingLogger<InboxScanner>());

    private static FakeFichierProcessor AlwaysSucceeds() =>
        new((name, _) => new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" });

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // AC-FR12-1: every Fichier in the inbox is processed from oldest to newest by LastWriteTimeUtc.
    // Names run counter to arrival order here, so a name sort would fail this assertion.
    [Fact]
    [Trait("AC", "FR12-1")]
    public void Tick_ProcessesFichiersOldestToNewest_AcFr12_1()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("c"), Now.AddMinutes(-1));
        source.Add("", "P60_847_682_003", Bytes("a"), Now.AddMinutes(-3));
        source.Add("", "P60_847_682_002", Bytes("b"), Now.AddMinutes(-2));
        FakeFichierProcessor processor = AlwaysSucceeds();

        Scanner(source, processor).RunTick();

        Assert.Equal(
            ["P60_847_682_003", "P60_847_682_002", "P60_847_682_001"],
            processor.Calls);
    }

    // AC-FR14-6: a cancelled token stops the tick between Fichiers - the one in flight finishes (never
    // half-processed), the rest stay untouched in the inbox for the next tick.
    [Fact]
    [Trait("AC", "FR14-6")]
    public void Tick_WhenTheTokenIsCancelledAfterAFichier_LeavesTheRestForTheNextTick_AcFr14_6()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("first"), Now.AddMinutes(-2));
        source.Add("", "P60_847_682_002", Bytes("second"), Now.AddMinutes(-1));

        using CancellationTokenSource cancellation = new();
        FakeFichierProcessor processor = new((name, _) =>
        {
            cancellation.Cancel();
            return new FichierProcessingResult { NormalizedXml = $"<file name=\"{name}\" />" };
        });

        Scanner(source, processor).RunTick(cancellation.Token);

        Assert.Equal(["P60_847_682_001"], processor.Calls);
        Assert.Contains("P60_847_682_002", source.Names(string.Empty));
        Assert.Empty(source.Names("processing"));
    }

    // AC-FR12-2: the Fichier is moved into processing/ before it is read, and it is that copy the
    // processor is handed - never the inbox original.
    [Fact]
    [Trait("AC", "FR12-2")]
    public void Tick_MovesFichierToProcessingBeforeReading_AcFr12_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));

        bool inProcessingWhenRead = false;
        bool goneFromInboxWhenRead = false;
        FakeFichierProcessor processor = new((name, content) =>
        {
            inProcessingWhenRead = source.Exists("processing", name);
            goneFromInboxWhenRead = !source.Exists("", name);
            Assert.Equal("payload", Encoding.UTF8.GetString(content));
            return new FichierProcessingResult { NormalizedXml = "<file />" };
        });

        Scanner(source, processor).RunTick();

        Assert.True(inProcessingWhenRead, "Fichier must sit in processing/ when the processor reads it.");
        Assert.True(goneFromInboxWhenRead, "Fichier must have left the inbox before it is read.");
    }

    // AC-FR12-3 (D11): a success moves the Fichier to archive/<yyyy>/<MM>/<name> and writes the
    // normalized XML next to it as <name>.xml.
    [Fact]
    [Trait("AC", "FR12-3")]
    public void Tick_OnSuccess_ArchivesFichierAndWritesNormalizedXml_AcFr12_3()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor =
            new((name, _) => new FichierProcessingResult { NormalizedXml = "<file>normalized</file>" });

        Scanner(source, processor).RunTick();

        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"), "Fichier must be archived under the dated folder.");
        Assert.False(source.Exists("", "P60_847_682_001"));
        Assert.False(source.Exists("processing", "P60_847_682_001"));
        Assert.Equal("<file>normalized</file>", Encoding.UTF8.GetString(source.Content(ArchiveDateFolder, "P60_847_682_001.xml")));
    }

    // AC-FR12-4: a rejection moves the Fichier to error/<name> and writes an <name>.errors.json holding
    // the Errors array next to it.
    [Fact]
    [Trait("AC", "FR12-4")]
    public void Tick_OnRejection_MovesFichierToErrorAndWritesErrorsJson_AcFr12_4()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        ConversionError error = new()
        {
            Block = Block.Header,
            Code = ErrorCode.RequiredFieldMissing,
            FieldId = "Client",
            LineNumber = 1,
            Message = "Le Champ obligatoire 'Client' de l'Entête est vide.",
        };
        FakeFichierProcessor processor = new((_, _) => new FichierProcessingResult { Errors = [error] });

        Scanner(source, processor).RunTick();

        Assert.True(source.Exists("error", "P60_847_682_001"), "rejected Fichier must land in error/.");
        Assert.False(source.Exists("", "P60_847_682_001"));
        Assert.False(source.Exists("processing", "P60_847_682_001"));

        using JsonDocument document = JsonDocument.Parse(source.Content("error", "P60_847_682_001.errors.json"));
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Contains("Client", document.RootElement[0].GetProperty("Message").GetString());
    }

    // AC-FR13-4: an unexpected exception from IFichierProcessor.Process on one Fichier is caught,
    // logged at Error, the Fichier is quarantined in error/ with an <name>.errors.json, and the tick
    // carries on to the next Fichier instead of unwinding.
    [Fact]
    [Trait("AC", "FR13-4")]
    public void Tick_ProcessorThrowsOnOneFichier_QuarantinesItAndKeepsProcessingTheRest_AcFr13_4()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("poison"), Now.AddMinutes(-2));
        source.Add("", "P60_847_682_002", Bytes("clean"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((name, _) => name == "P60_847_682_001"
            ? throw new InvalidOperationException("boom")
            : new FichierProcessingResult { NormalizedXml = "<file />" });
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger: logger).RunTick();

        Assert.True(source.Exists("error", "P60_847_682_001"), "the poison Fichier must land in error/.");
        Assert.True(source.Exists("error", "P60_847_682_001.errors.json"));
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_002"), "the next Fichier must still be processed.");
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // AC-FR12-5: a Fichier whose size is still changing between two probes is left in the inbox,
    // not processed, and nothing is logged as a Warning or an Error.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_LeavesHalfWrittenFichierInInbox_WithoutLoggingAnError_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("still uploading"), Now.AddMinutes(-1));
        source.MarkUnstableOnce("P60_847_682_001");
        FakeFichierProcessor processor = AlwaysSucceeds();
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger: logger).RunTick();

        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"), "half-written Fichier must stay in the inbox.");
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // AC-FR12-5 (second tick): once the size settles, the next tick processes the Fichier.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_ProcessesFichierOnceItsSizeSettles_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("done"), Now.AddMinutes(-1));
        source.MarkUnstableOnce("P60_847_682_001");
        FakeFichierProcessor processor = AlwaysSucceeds();
        InboxScanner scanner = Scanner(source, processor);

        scanner.RunTick();
        scanner.RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
    }

    // AC-FR12-6: a Fichier stranded in processing/ by a killed worker is picked up again on the next
    // tick (the anti-duplicate guard of Story 2.8 is what prevents a second insert; that is the
    // persister's concern, not the scanner's).
    [Fact]
    [Trait("AC", "FR12-6")]
    public void Tick_ResumesFichierStrandedInProcessing_AcFr12_6()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("resumed"), Now.AddMinutes(-10));
        FakeFichierProcessor processor = AlwaysSucceeds();

        Scanner(source, processor).RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
        Assert.False(source.Exists("processing", "P60_847_682_001"));
    }

    // AC-FR12-7: an empty inbox makes a tick a no-op - no processor call, no exception, nothing logged
    // above Information.
    [Fact]
    [Trait("AC", "FR12-7")]
    public void Tick_OnEmptyInbox_DoesNothing_AcFr12_7()
    {
        InMemoryFileSource source = new();
        FakeFichierProcessor processor = AlwaysSucceeds();
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger: logger).RunTick();

        Assert.Empty(processor.Calls);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // AC-FR12-9 (D13): the purge deletes files in archive/ and error/ older than RetentionDays and
    // leaves younger ones untouched.
    [Fact]
    [Trait("AC", "FR12-9")]
    public void Purge_DeletesArchiveAndErrorFilesPastRetention_AcFr12_9()
    {
        InMemoryFileSource source = new();
        source.Add(ArchiveDateFolder, "old.xml", Bytes("x"), Now.AddDays(-31));
        source.Add(ArchiveDateFolder, "recent.xml", Bytes("x"), Now.AddDays(-2));
        source.Add("error", "old.errors.json", Bytes("[]"), Now.AddDays(-40));
        source.Add("error", "recent.errors.json", Bytes("[]"), Now.AddDays(-1));

        Scanner(source, AlwaysSucceeds()).PurgeRetention();

        Assert.False(source.Exists(ArchiveDateFolder, "old.xml"));
        Assert.False(source.Exists("error", "old.errors.json"));
        Assert.True(source.Exists(ArchiveDateFolder, "recent.xml"));
        Assert.True(source.Exists("error", "recent.errors.json"));
    }

    // AC-FR12-9: RetentionDays <= 0 disables the purge entirely.
    [Fact]
    [Trait("AC", "FR12-9")]
    public void Purge_IsDisabled_WhenRetentionDaysNotPositive_AcFr12_9()
    {
        InMemoryFileSource source = new();
        source.Add(ArchiveDateFolder, "ancient.xml", Bytes("x"), Now.AddYears(-5));
        ImportOptions options = Options();
        options.RetentionDays = 0;

        Scanner(source, AlwaysSucceeds(), options).PurgeRetention();

        Assert.True(source.Exists(ArchiveDateFolder, "ancient.xml"), "purge must be off when RetentionDays <= 0.");
    }

    // AC-FR12-9: a blank ArchiveFolder / ErrorFolder is skipped, so the purge never walks the whole
    // reception root and deletes aged inbox Fichiers.
    [Fact]
    [Trait("AC", "FR12-9")]
    public void Purge_SkipsAFolderWhoseNameIsBlank_AcFr12_9()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("x"), Now.AddDays(-99));
        ImportOptions options = Options();
        options.ArchiveFolder = string.Empty;
        options.ErrorFolder = string.Empty;

        Scanner(source, AlwaysSucceeds(), options).PurgeRetention();

        Assert.True(source.Exists("", "P60_847_682_001"), "an aged inbox Fichier must survive a blank-folder purge.");
    }
}
