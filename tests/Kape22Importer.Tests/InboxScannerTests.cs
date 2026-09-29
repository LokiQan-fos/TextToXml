using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
// Story 6.3 / FR-26 adds the XML export folder: it is an absolute path IFileSource does not reach, so those
// tests write to a real temp folder, deleted after each test.
[Trait("Category", TestCategory.Unit)]
public sealed class InboxScannerTests : IDisposable
{
    // A fixed summer instant: Paris is UTC+2, so the archive date folder is 2026/09.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    // The export file name suffix for Now, in Paris time (UTC+2 in September).
    private const string NowSuffix = "20260908120000";

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
        RecordingLogger<InboxScanner>? logger = null,
        DateTimeOffset? now = null) =>
        new(fileSource, processor, options ?? Options(), new FixedClock(now ?? Now), logger ?? new RecordingLogger<InboxScanner>());

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

    // AC-FR12-5: a Fichier written more recently than Import:StabilityQuietPeriod may still be copied
    // in; it is left in the inbox, not processed, and nothing is logged as a Warning or an Error.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_LeavesFichierWrittenWithinTheQuietPeriodInInbox_WithoutLoggingAWarning_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("still uploading"), Now.AddSeconds(-5));
        FakeFichierProcessor processor = AlwaysSucceeds();
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, processor, logger: logger).RunTick();

        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"), "a Fichier still within its quiet period must stay in the inbox.");
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    // AC-FR12-5 (next tick): once the quiet period has fully elapsed since the last write, the Fichier is
    // processed.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_ProcessesFichierOnceTheQuietPeriodHasElapsed_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("done"), Now.AddSeconds(-5));
        FakeFichierProcessor processor = AlwaysSucceeds();

        Scanner(source, processor).RunTick();
        Assert.Empty(processor.Calls);

        Scanner(source, processor, now: Now.AddSeconds(5)).RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
    }

    // AC-FR12-5: the gate is per Fichier - a settled Fichier is processed in the same tick that leaves a
    // younger one in the inbox.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_MixedInbox_ProcessesOnlyTheSettledFichier_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("settled"), Now.AddMinutes(-1));
        source.Add("", "P60_847_682_002", Bytes("young"), Now.AddSeconds(-1));
        FakeFichierProcessor processor = AlwaysSucceeds();

        Scanner(source, processor).RunTick();

        Assert.Equal(["P60_847_682_001"], processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_002"), "the younger Fichier must stay in the inbox.");
    }

    // AC-FR12-5: a last write after the clock's now (clock skew with the share) is not settled either.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_FichierWithALastWriteInTheFuture_IsLeftInInbox_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("skewed"), Now.AddMinutes(1));
        FakeFichierProcessor processor = AlwaysSucceeds();

        Scanner(source, processor).RunTick();

        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"));
    }

    // AC-FR12-5: a configured Import:StabilityQuietPeriod replaces the default - a Fichier older than
    // 10 seconds but younger than the configured minute is still left in the inbox.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void Tick_ConfiguredQuietPeriod_LeavesAFichierYoungerThanItInInbox_AcFr12_5()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("slow share"), Now.AddSeconds(-30));
        FakeFichierProcessor processor = AlwaysSucceeds();
        ImportOptions options = Options();
        options.StabilityQuietPeriod = TimeSpan.FromMinutes(1);

        Scanner(source, processor, options).RunTick();

        Assert.Empty(processor.Calls);
        Assert.True(source.Exists("", "P60_847_682_001"));
    }

    // AC-FR12-5: Import:StabilityQuietPeriod defaults to 10 seconds when it is not configured.
    [Fact]
    [Trait("AC", "FR12-5")]
    public void ImportOptions_StabilityQuietPeriod_DefaultsToTenSeconds_AcFr12_5()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new ImportOptions().StabilityQuietPeriod);
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

    // Story 6.3 / FR-26: the real temp export folder, one per test.
    private readonly string exportFolder = Path.Combine(Path.GetTempPath(), "kape22-export-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.exportFolder))
        {
            Directory.Delete(this.exportFolder, recursive: true);
        }
    }

    private ImportOptions ExportOptions()
    {
        ImportOptions options = Options();
        options.XmlExportPath = this.exportFolder;
        return options;
    }

    private string[] ExportNames() =>
        Directory.Exists(this.exportFolder)
            ? [.. Directory.GetFiles(this.exportFolder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)]
            : [];

    private static ConversionError MapperError() => new()
    {
        Block = Block.Header,
        Code = ErrorCode.RequiredFieldMissing,
        FieldId = "Client",
        LineNumber = 1,
        Message = "Le Champ obligatoire 'Client' de l'Entête est vide.",
    };

    // AC-FR26-1: every Fichier whose outcome is final and that converted leaves <name>_<yyyyMMddHHmmss>.xml
    // (Paris time) in Import:XmlExportPath - imported, rejected after conversion (mapping, business
    // controls, SQL), or archived by the D22 guard (a success result with a Warning at this seam) - and is
    // still filed in archive/ or error/ with its sidecar XML as before.
    [Theory]
    [InlineData("imported")]
    [InlineData("rejected")]
    [InlineData("d22-guard")]
    [Trait("AC", "FR26-1")]
    public void Tick_ConvertedFichier_WritesTheXmlToTheExportFolder_AcFr26_1(string outcome)
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FichierProcessingResult result = outcome switch
        {
            "rejected" => new() { Errors = [MapperError()], NormalizedXml = "<file>normalized</file>" },
            "d22-guard" => new() { NormalizedXml = "<file>normalized</file>", Warnings = [MapperError()] },
            _ => new() { NormalizedXml = "<file>normalized</file>" },
        };

        Scanner(source, new FakeFichierProcessor((_, _) => result), this.ExportOptions()).RunTick();

        Assert.Equal([$"P60_847_682_001_{NowSuffix}.xml"], this.ExportNames());
        Assert.Equal(
            "<file>normalized</file>",
            File.ReadAllText(Path.Combine(this.exportFolder, $"P60_847_682_001_{NowSuffix}.xml")));
        string filedIn = outcome == "rejected" ? "error" : ArchiveDateFolder;
        Assert.True(source.Exists(filedIn, "P60_847_682_001"));
        Assert.True(source.Exists(filedIn, "P60_847_682_001.xml"));
    }

    // AC-FR26-2: a Fichier that failed conversion (Step 1, no XML) or whose processing threw leaves nothing
    // in the export folder; it is still rejected to error/.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("AC", "FR26-2")]
    public void Tick_FichierWithoutXml_LeavesNothingInTheExportFolder_AcFr26_2(bool processorThrows)
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = new((_, _) => processorThrows
            ? throw new InvalidOperationException("boom")
            : new FichierProcessingResult { Errors = [MapperError()] });

        Scanner(source, processor, this.ExportOptions()).RunTick();

        Assert.Empty(this.ExportNames());
        Assert.True(source.Exists("error", "P60_847_682_001"));
    }

    // AC-FR26-2 (Story 6.3 scope): a PersistenceError outcome is not final - the Fichier stays in
    // processing/ for a retry and nothing is exported until it is.
    [Fact]
    [Trait("AC", "FR26-2")]
    public void Tick_PersistenceFailure_ExportsNothing_AcFr26_2()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        ConversionError unreachable = new()
        {
            Block = Block.File,
            Code = ErrorCode.PersistenceError,
            Message = "AscoLSI unreachable.",
        };
        FakeFichierProcessor processor =
            new((_, _) => new FichierProcessingResult { Errors = [unreachable], NormalizedXml = "<file />" });

        Scanner(source, processor, this.ExportOptions()).RunTick();

        Assert.Empty(this.ExportNames());
        Assert.True(source.Exists("processing", "P60_847_682_001"));
    }

    // AC-FR26-3: the same Fichier name processed at two instants (injected clock) yields two export files;
    // neither overwrites the other.
    [Fact]
    [Trait("AC", "FR26-3")]
    public void Tick_SameNameAtTwoInstants_WritesTwoExportFiles_AcFr26_3()
    {
        InMemoryFileSource source = new();
        FakeFichierProcessor processor = AlwaysSucceeds();

        source.Add("", "P60_847_682_001", Bytes("first"), Now.AddMinutes(-1));
        Scanner(source, processor, this.ExportOptions()).RunTick();
        source.Add("", "P60_847_682_001", Bytes("second"), Now.AddMinutes(-1));
        Scanner(source, processor, this.ExportOptions(), now: Now.AddSeconds(1)).RunTick();

        Assert.Equal(
            ["P60_847_682_001_20260908120000.xml", "P60_847_682_001_20260908120001.xml"],
            this.ExportNames());
    }

    // AC-FR26-4: the Import:RetentionDays purge never touches the export folder, however old its files.
    [Fact]
    [Trait("AC", "FR26-4")]
    public void Purge_LeavesTheExportFolderUntouched_AcFr26_4()
    {
        Directory.CreateDirectory(this.exportFolder);
        string aged = Path.Combine(this.exportFolder, "P60_847_682_001_20200101000000.xml");
        File.WriteAllText(aged, "<file />");
        File.SetLastWriteTimeUtc(aged, Now.AddYears(-5).UtcDateTime);

        Scanner(new InMemoryFileSource(), AlwaysSucceeds(), this.ExportOptions()).PurgeRetention();

        Assert.True(File.Exists(aged), "the purge must never delete an export file.");
    }

    // AC-FR26-6: an export that cannot be written - the export folder unreachable (a file stands where the
    // folder should be), or the target name already taken - leaves the Fichier in processing/, logs a
    // Warning, files nothing in archive/, and never overwrites nor deletes the existing file.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("AC", "FR26-6")]
    public void Tick_ExportUnwritable_LeavesTheFichierInProcessing_AcFr26_6(bool nameTaken)
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        ImportOptions options = this.ExportOptions();
        Directory.CreateDirectory(this.exportFolder);
        string existing = nameTaken
            ? Path.Combine(this.exportFolder, $"P60_847_682_001_{NowSuffix}.xml")
            : Path.Combine(this.exportFolder, "not-a-folder");
        if (!nameTaken)
        {
            options.XmlExportPath = existing;
        }

        File.WriteAllText(existing, "earlier");
        RecordingLogger<InboxScanner> logger = new();

        Scanner(source, AlwaysSucceeds(), options, logger).RunTick();

        Assert.True(source.Exists("processing", "P60_847_682_001"), "the Fichier must stay in processing/.");
        Assert.Empty(source.Names(ArchiveDateFolder));
        Assert.Equal("earlier", File.ReadAllText(existing));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // AC-FR26-6: once the export is written, a failure to file the outcome (archive/ unreachable) deletes it
    // again, so the retry next tick leaves a single export file; the Fichier stays in processing/ meanwhile.
    [Fact]
    [Trait("AC", "FR26-6")]
    public void Tick_FilingFailsAfterTheExport_DeletesTheExportAndRetriesCleanly_AcFr26_6()
    {
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));
        FakeFichierProcessor processor = AlwaysSucceeds();
        RecordingLogger<InboxScanner> logger = new();
        source.MoveFault = new IOException("archive unreachable");

        Scanner(source, processor, this.ExportOptions(), logger).RunTick();

        Assert.Empty(this.ExportNames());
        Assert.True(source.Exists("processing", "P60_847_682_001"));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);

        source.MoveFault = null;
        Scanner(source, processor, this.ExportOptions(), now: Now.AddSeconds(30)).RunTick();

        Assert.Equal(["P60_847_682_001_20260908120030.xml"], this.ExportNames());
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
    }

    // Library level: an empty Import:XmlExportPath turns the export off (GpaoImportP60 refuses to start
    // without it, AC-FR26-5); the Fichier is archived as before.
    [Fact]
    [Trait("AC", "FR26-1")]
    public void Tick_NoExportFolderConfigured_ArchivesWithoutExport_AcFr26_1()
    {
        InMemoryFileSource source = new();
        source.Add("", "P60_847_682_001", Bytes("payload"), Now.AddMinutes(-1));

        Scanner(source, AlwaysSucceeds()).RunTick();

        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
        Assert.Empty(this.ExportNames());
    }
}
