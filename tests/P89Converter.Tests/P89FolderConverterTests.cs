using System;
using System.IO;
using System.Linq;
using System.Threading;
using FichierJournal;
using TextToXml.Tests;
using static P89Converter.Tests.TestSupport;

namespace P89Converter.Tests;

// One tick over the P89 source folder (Story 5.1): each Fichier is converted, its XML written under a
// timestamped name, one IFichierJournal entry recorded, then the Fichier is moved to the done or error
// folder. Temp folders and a recording journal, no database. Written test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public sealed class P89FolderConverterTests : IDisposable
{
    private readonly TempFolders folders = new();
    private readonly RecordingJournal journal = new();

    public void Dispose() => this.folders.Dispose();

    // AC-FR22-5: a converted Fichier leaves the source folder for done under <name>_<yyyyMMddHHmmss>,
    // next to its XML <name>_<yyyyMMddHHmmss>.xml, with one entry without reason carrying the raw
    // zero-padded NumeroFichier and OF.
    [Fact]
    [Trait("AC", "FR22-5")]
    public void RunTick_ReferenceFichiers_WritesXmlRecordsEntryAndMovesToDone_AcFr22_5()
    {
        foreach (string name in ReferenceFichierNames)
        {
            this.folders.Drop(name, ReadFixture(name));
        }

        var outcomes = this.Converter(Instant).RunTick();

        Assert.All(outcomes, outcome => Assert.Equal(P89FichierStatus.Converted, outcome.Status));
        Assert.Empty(this.folders.Names(this.folders.Source));
        Assert.Equal(ReferenceFichierNames.Select(name => $"{name}_{InstantSuffix}"), this.folders.Names(this.folders.Done));
        Assert.Equal(ReferenceFichierNames.Select(name => $"{name}_{InstantSuffix}.xml"), this.folders.Names(this.folders.Xml));
        Assert.Empty(this.folders.Names(this.folders.Error));

        Assert.Equal(ReferenceFichierNames, this.journal.Entries.Select(entry => entry.FichierName));
        Assert.All(this.journal.Entries, entry =>
        {
            Assert.Equal("P89", entry.Commande);
            Assert.Equal(Instant, entry.Instant);
            Assert.False(string.IsNullOrWhiteSpace(entry.OF));
            Assert.Empty(entry.Reasons);
        });
        Assert.Equal("013", this.journal.Entries[1].NumeroFichier);
    }

    // AC-FR16-5 (D33, Story 6.3): P89 already has its own XML export folder, P89:XmlPath (kept distinct
    // from the working folders by GpaoConvertP89, AC-FR22-8); a converted Fichier leaves
    // <name>_<yyyyMMddHHmmss>.xml there and nowhere else, and P89FolderConverter has no purge.
    // Conformance check, green on arrival: no production change.
    [Fact]
    [Trait("AC", "FR16-5")]
    public void RunTick_ConvertedFichier_LeavesItsXmlInTheDedicatedExportFolder_AcFr16_5()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));

        this.Converter(Instant).RunTick();

        Assert.Equal([$"{name}_{InstantSuffix}.xml"], this.folders.Names(this.folders.Xml));
        Assert.DoesNotContain(
            new[] { this.folders.Source, this.folders.Done, this.folders.Error }.SelectMany(this.folders.Names),
            file => file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    // AC-FR22-5 (F-1 of the Story 5.0 review): a blank NumeroFichier or OF still converts; the Fichier goes
    // to done and its entry carries null, leaving the fallback (or the D15 skip) to the journal.
    [Theory]
    [InlineData("NumeroFichier")]
    [InlineData("OF")]
    [Trait("AC", "FR22-5")]
    public void RunTick_BlankNumeroFichierOrOf_MovesToDoneAndRecordsANullValue_AcFr22_5(string champ)
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, BlankChampFichier(champ));

        P89FichierOutcome outcome = Assert.Single(this.Converter(Instant).RunTick());

        Assert.Equal(P89FichierStatus.Converted, outcome.Status);
        Assert.Equal([$"{name}_{InstantSuffix}"], this.folders.Names(this.folders.Done));
        FichierJournalEntry entry = Assert.Single(this.journal.Entries);
        Assert.Null(champ == "OF" ? entry.OF : entry.NumeroFichier);
    }

    // AC-FR22-4: the same Fichier name converted at two instants (index rotation 999 -> 001) yields two
    // XML files and two done files; nothing is overwritten.
    [Fact]
    [Trait("AC", "FR22-4")]
    public void RunTick_SameNameAtTwoInstants_NeverOverwrites_AcFr22_4()
    {
        string name = ReferenceFichierNames[0];

        this.folders.Drop(name, ReadFixture(name));
        this.Converter(Instant).RunTick();
        this.folders.Drop(name, ReadFixture(name));
        this.Converter(Instant.AddDays(30)).RunTick();

        Assert.Equal([$"{name}_20260210080000.xml", $"{name}_20260312080000.xml"], this.folders.Names(this.folders.Xml));
        Assert.Equal([$"{name}_20260210080000", $"{name}_20260312080000"], this.folders.Names(this.folders.Done));
    }

    // AC-FR22-4: an existing XML under the target name is never overwritten; the Fichier stays in the
    // source folder for the next tick, and nothing is recorded.
    [Fact]
    [Trait("AC", "FR22-4")]
    public void RunTick_TargetXmlAlreadyExists_LeavesItAndTheFichierUntouched_AcFr22_4()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        Directory.CreateDirectory(this.folders.Xml);
        string existing = Path.Combine(this.folders.Xml, $"{name}_{InstantSuffix}.xml");
        File.WriteAllText(existing, "earlier");

        P89FichierOutcome outcome = Assert.Single(this.Converter(Instant).RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Equal("earlier", File.ReadAllText(existing));
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Empty(this.journal.Entries);
    }

    // AC-FR22-4: a done target that already exists is never overwritten: it is checked before anything
    // is written, so no XML, no entry, and the Fichier stays in the source folder for the next tick.
    [Fact]
    [Trait("AC", "FR22-4")]
    public void RunTick_DoneTargetAlreadyExists_DefersWithoutXmlNorEntry_AcFr22_4()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        Directory.CreateDirectory(this.folders.Done);
        string existing = Path.Combine(this.folders.Done, $"{name}_{InstantSuffix}");
        File.WriteAllText(existing, "earlier");

        P89FichierOutcome outcome = Assert.Single(this.Converter(Instant).RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Equal("earlier", File.ReadAllText(existing));
        Assert.Empty(this.journal.Entries);
    }

    // AC-FR22-4: an error target that already exists is never overwritten either: the rejected Fichier is
    // deferred before its entry, so a retry never records a second one.
    [Fact]
    [Trait("AC", "FR22-4")]
    public void RunTick_ErrorTargetAlreadyExists_DefersWithoutEntryAndKeepsTheEarlierFile_AcFr22_4()
    {
        string name = "LP89_682_617_900";
        this.folders.Drop(name, InvalidUtf8Fichier());
        Directory.CreateDirectory(this.folders.Error);
        string existing = Path.Combine(this.folders.Error, $"{name}_{InstantSuffix}");
        File.WriteAllText(existing, "earlier");

        P89FichierOutcome outcome = Assert.Single(this.Converter(Instant).RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Equal("earlier", File.ReadAllText(existing));
        Assert.Empty(this.journal.Entries);
    }

    // AC-FR22-4: the suffix is the host local wall time, while the entry Instant stays the UTC instant.
    [Fact]
    [Trait("AC", "FR22-4")]
    public void RunTick_LocalZoneUtcPlusOne_SuffixesWithLocalWallTime_AcFr22_4()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        TimeZoneInfo utcPlusOne = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");

        new P89FolderConverter(this.Options(), this.journal, new FixedClock(Instant, utcPlusOne)).RunTick();

        Assert.Equal([$"{name}_20260210090000"], this.folders.Names(this.folders.Done));
        Assert.Equal([$"{name}_20260210090000.xml"], this.folders.Names(this.folders.Xml));
        Assert.Equal(Instant, Assert.Single(this.journal.Entries).Instant);
    }

    // AC-FR22-6 (with AC-FR22-3): an encoding or conversion failure writes no XML; the Fichier goes to
    // error under the timestamped name and one entry carries its reasons, with no OF (unreadable).
    [Theory]
    [InlineData("invalid-utf8")]
    [InlineData("outside-1252")]
    [InlineData("truncated")]
    [Trait("AC", "FR22-6")]
    public void RunTick_UnreadableOf_MovesToErrorAndRecordsEntryWithoutOf_AcFr22_6(string variant)
    {
        byte[] content = variant switch
        {
            "invalid-utf8" => InvalidUtf8Fichier(),
            "outside-1252" => OutsideWindows1252Fichier(),
            _ => TruncatedFichier(),
        };
        this.folders.Drop("LP89_682_617_900", content);

        P89FichierOutcome outcome = Assert.Single(this.Converter(Instant).RunTick());

        Assert.Equal(P89FichierStatus.Rejected, outcome.Status);
        Assert.NotEmpty(outcome.Reasons);
        Assert.Equal([$"LP89_682_617_900_{InstantSuffix}"], this.folders.Names(this.folders.Error));
        Assert.Empty(this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Xml));

        FichierJournalEntry entry = Assert.Single(this.journal.Entries);
        Assert.Equal("LP89_682_617_900", entry.FichierName);
        Assert.Null(entry.OF);
        Assert.Equal(outcome.Reasons, entry.Reasons);
        if (variant != "truncated")
        {
            Assert.StartsWith("Encodage", Assert.Single(entry.Reasons), StringComparison.Ordinal);
        }
    }

    // AC-FR22-6: a schema-invalid XML is not written, but its OF is readable, so the entry carries the XSD
    // reasons and the OF, and the Fichier goes to error.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void RunTick_SchemaInvalidXml_RecordsXsdReasonsWithOfAndMovesToError_AcFr22_6()
    {
        this.folders.Drop(AccentedFichierName, ReadFixture(AccentedFichierName));

        P89FolderConverter converter = new(
            this.Options(),
            this.journal,
            new FixedClock(Instant),
            content => P89FichierConverter.Convert(content, P89FichierConverterTests.StricterSchema()));
        P89FichierOutcome outcome = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Rejected, outcome.Status);
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Equal([$"{AccentedFichierName}_{InstantSuffix}"], this.folders.Names(this.folders.Error));
        FichierJournalEntry entry = Assert.Single(this.journal.Entries);
        Assert.Equal("013", entry.NumeroFichier);
        Assert.False(string.IsNullOrWhiteSpace(entry.OF));
        Assert.NotEmpty(entry.Reasons);
        Assert.All(entry.Reasons, reason => Assert.StartsWith("XSD", reason, StringComparison.Ordinal));
    }

    // AC-FR22-6: one failing Fichier does not stop the others (no fail-fast); each gets its entry.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void RunTick_MixedFolder_ProcessesEveryFichier_AcFr22_6()
    {
        this.folders.Drop("LP89_682_617_000", InvalidUtf8Fichier());
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));
        this.folders.Drop(ReferenceFichierNames[2], ReadFixture(ReferenceFichierNames[2]));

        var outcomes = this.Converter(Instant).RunTick();

        Assert.Equal(3, outcomes.Count);
        Assert.Equal(2, this.folders.Names(this.folders.Done).Length);
        Assert.Single(this.folders.Names(this.folders.Error));
        Assert.Empty(this.folders.Names(this.folders.Source));
        Assert.Equal(3, this.journal.Entries.Count);
    }

    // AC-FR22-6: a journal that throws defers the Fichier: the XML just written is deleted and the Fichier
    // stays in the source folder, retried at the next tick, with the journal failure as its reason.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void RunTick_JournalThrows_DeletesXmlAndLeavesFichierInSource_AcFr22_6()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));

        P89FolderConverter converter = this.Converter(Instant, new RecordingJournal(new IOException("unreachable")));
        P89FichierOutcome outcome = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Contains(outcome.Reasons, reason => reason.StartsWith("Journal", StringComparison.Ordinal));
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Empty(this.folders.Names(this.folders.Done));
        Assert.Empty(this.folders.Names(this.folders.Error));
    }

    // AC-FR22-6: any exception from Record (here a connection-pool timeout) is contained: the Fichier is
    // deferred and the remaining Fichiers of the tick still run.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void RunTick_JournalThrowsAnyException_DefersAndKeepsTheTickGoing_AcFr22_6()
    {
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));
        this.folders.Drop(ReferenceFichierNames[2], ReadFixture(ReferenceFichierNames[2]));

        P89FolderConverter converter =
            this.Converter(Instant, new RecordingJournal(new InvalidOperationException("pool timeout")));
        var outcomes = converter.RunTick();

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.Equal(P89FichierStatus.Deferred, outcome.Status));
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Equal(2, this.folders.Names(this.folders.Source).Length);
        Assert.Empty(this.folders.Names(this.folders.Error));
    }

    // AC-FR22-6: a rejected Fichier whose entry cannot be recorded is deferred, not moved to error, so its
    // entry is never lost; the reasons carry both the XSD cause and the journal cause.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void RunTick_SchemaInvalidXmlWithJournalThrowing_DefersWithBothReasons_AcFr22_6()
    {
        this.folders.Drop(AccentedFichierName, ReadFixture(AccentedFichierName));

        P89FolderConverter converter = new(
            this.Options(),
            new RecordingJournal(new InvalidOperationException("unreachable")),
            new FixedClock(Instant),
            content => P89FichierConverter.Convert(content, P89FichierConverterTests.StricterSchema()));
        P89FichierOutcome outcome = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Contains(outcome.Reasons, reason => reason.StartsWith("XSD", StringComparison.Ordinal));
        Assert.Contains(outcome.Reasons, reason => reason.StartsWith("Journal", StringComparison.Ordinal));
        Assert.Equal([AccentedFichierName], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Error));
    }

    // AC-FR22-9: a Fichier written more recently than P89:StabilityQuietPeriod may still be copied in; it
    // is left in the source folder with no XML, no entry and no outcome.
    [Fact]
    [Trait("AC", "FR22-9")]
    public void RunTick_FichierWrittenWithinTheQuietPeriod_IsLeftInSourceWithoutXmlNorEntry_AcFr22_9()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name), Instant.AddSeconds(-5));

        Assert.Empty(this.Converter(Instant).RunTick());

        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Empty(this.folders.Names(this.folders.Done));
        Assert.Empty(this.journal.Entries);
    }

    // AC-FR22-9 (next tick): once the quiet period has fully elapsed since the last write, the Fichier is
    // converted; a settled Fichier of the same tick is not held back by a younger one.
    [Fact]
    [Trait("AC", "FR22-9")]
    public void RunTick_FichierOnceTheQuietPeriodHasElapsed_IsConverted_AcFr22_9()
    {
        string young = ReferenceFichierNames[0];
        string settled = ReferenceFichierNames[2];
        this.folders.Drop(young, ReadFixture(young), Instant.AddSeconds(-5));
        this.folders.Drop(settled, ReadFixture(settled));

        P89FichierOutcome first = Assert.Single(this.Converter(Instant).RunTick());
        P89FichierOutcome next = Assert.Single(this.Converter(Instant.AddSeconds(5)).RunTick());

        Assert.Equal((settled, P89FichierStatus.Converted), (first.FichierName, first.Status));
        Assert.Equal((young, P89FichierStatus.Converted), (next.FichierName, next.Status));
        Assert.Empty(this.folders.Names(this.folders.Source));
    }

    // AC-FR22-9: a last write after the clock's now (clock skew with the share) is not settled either.
    [Fact]
    [Trait("AC", "FR22-9")]
    public void RunTick_FichierWithALastWriteInTheFuture_IsLeftInSource_AcFr22_9()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name), Instant.AddMinutes(1));

        Assert.Empty(this.Converter(Instant).RunTick());
        Assert.Equal([name], this.folders.Names(this.folders.Source));
    }

    // AC-FR22-9: a configured P89:StabilityQuietPeriod replaces the default - a Fichier older than
    // 10 seconds but younger than the configured minute is still left in the source folder.
    [Fact]
    [Trait("AC", "FR22-9")]
    public void RunTick_ConfiguredQuietPeriod_LeavesAFichierYoungerThanItInSource_AcFr22_9()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name), Instant.AddSeconds(-30));
        P89Options options = this.Options();
        options.StabilityQuietPeriod = TimeSpan.FromMinutes(1);

        Assert.Empty(new P89FolderConverter(options, this.journal, new FixedClock(Instant)).RunTick());
        Assert.Equal([name], this.folders.Names(this.folders.Source));
    }

    // AC-FR22-9: P89:StabilityQuietPeriod defaults to 10 seconds when it is not configured.
    [Fact]
    [Trait("AC", "FR22-9")]
    public void P89Options_StabilityQuietPeriod_DefaultsToTenSeconds_AcFr22_9()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new P89Options().StabilityQuietPeriod);
    }

    // A file-system fault (source locked, unreadable) defers the Fichier without any entry. Its reason is the
    // bare I/O message, unchanged by Story 6.12: no exception type name.
    [Fact]
    public void RunTick_SourceUnreadable_DefersWithoutEntry()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));

        P89FichierOutcome outcome;
        string message;
        using (new FileStream(Path.Combine(this.folders.Source, name), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            message = Assert.Throws<IOException>(() => File.ReadAllBytes(Path.Combine(this.folders.Source, name))).Message;
            outcome = Assert.Single(this.Converter(Instant).RunTick());
        }

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Equal("Fichier : " + message, Assert.Single(outcome.Reasons));
        Assert.DoesNotContain(nameof(IOException), outcome.Reasons[0], StringComparison.Ordinal);
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Xml));
        Assert.Empty(this.journal.Entries);
    }

    // Only LP89_* Fichiers are picked up; any other file in the source folder is left alone.
    [Fact]
    public void RunTick_FileNotNamedLp89_IsLeftInTheSourceFolder()
    {
        this.folders.Drop("README.txt", ReadFixture(ReferenceFichierNames[0]));

        Assert.Empty(this.Converter(Instant).RunTick());
        Assert.Equal(["README.txt"], this.folders.Names(this.folders.Source));
    }

    // NFR-9: a cancelled token is checked before each Fichier, so a Launcher stop processes nothing more.
    [Fact]
    public void RunTick_CancelledToken_ProcessesNothing()
    {
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));

        var outcomes = this.Converter(Instant).RunTick(new CancellationToken(canceled: true));

        Assert.Empty(outcomes);
        Assert.Single(this.folders.Names(this.folders.Source));
    }

    // AC-FR25-8: N consecutive Deferred ticks (here a failing journal, N = 3): ticks 1..N-1 are Deferred and
    // tick N is one Frozen outcome carrying the same reasons; the Fichier stays in the source folder and is
    // never moved to error.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_DeferredForMaxAttemptsTicks_ReportsOneFrozenOutcome_AcFr25_8()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        P89FolderConverter converter = this.CappedConverter(new RecordingJournal(new IOException("unreachable")));

        P89FichierOutcome first = Assert.Single(converter.RunTick());
        P89FichierOutcome second = Assert.Single(converter.RunTick());
        P89FichierOutcome third = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Deferred, first.Status);
        Assert.Equal(P89FichierStatus.Deferred, second.Status);
        Assert.Equal(P89FichierStatus.Frozen, third.Status);
        Assert.Equal(name, third.FichierName);
        Assert.Contains(third.Reasons, reason => reason.Contains("unreachable", StringComparison.Ordinal));
        Assert.Equal([name], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Error));
        Assert.Empty(this.folders.Names(this.folders.Xml));
    }

    // AC-FR25-8 (Story 6.12, D32): a non-I/O exception converting one Fichier is a Deferred outcome naming the
    // exception type and message, so the next Fichier is converted on the same tick and the faulty one
    // freezes at the cap, never leaving the source folder.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_UnexpectedConversionException_DefersTheFichierAndConvertsTheNext_AcFr25_8()
    {
        string faulty = ReferenceFichierNames[0];
        string healthy = ReferenceFichierNames[1];
        byte[] faultyContent = ReadFixture(faulty);
        this.folders.Drop(faulty, faultyContent);
        this.folders.Drop(healthy, ReadFixture(healthy));
        P89Options options = this.Options();
        options.MaxAttempts = 3;
        P89FolderConverter converter = new(
            options,
            this.journal,
            new FixedClock(Instant),
            content => content.AsSpan().SequenceEqual(faultyContent)
                ? throw new InvalidOperationException("conversion went wrong")
                : P89FichierConverter.Convert(content));

        P89FichierOutcome[] first = [.. converter.RunTick()];
        converter.RunTick();
        P89FichierOutcome third = Assert.Single(converter.RunTick());

        P89FichierOutcome deferred = first.Single(outcome => outcome.FichierName == faulty);
        Assert.Equal(P89FichierStatus.Deferred, deferred.Status);
        string firstReason = Assert.Single(deferred.Reasons);
        Assert.Contains(nameof(InvalidOperationException), firstReason, StringComparison.Ordinal);
        Assert.Contains("conversion went wrong", firstReason, StringComparison.Ordinal);
        Assert.Equal(P89FichierStatus.Converted, first.Single(outcome => outcome.FichierName == healthy).Status);
        Assert.Equal(P89FichierStatus.Frozen, third.Status);
        Assert.Equal(faulty, third.FichierName);
        string reason = Assert.Single(third.Reasons);
        Assert.Contains(nameof(InvalidOperationException), reason, StringComparison.Ordinal);
        Assert.Contains("conversion went wrong", reason, StringComparison.Ordinal);
        Assert.Equal([faulty], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Error));
        Assert.Equal([healthy], this.journal.Entries.Select(entry => entry.FichierName));
        Assert.Equal([$"{healthy}_{InstantSuffix}.xml"], this.folders.Names(this.folders.Xml));
        Assert.Equal([$"{healthy}_{InstantSuffix}"], this.folders.Names(this.folders.Done));
    }

    // AC-FR25-8: from tick N+1 a frozen Fichier still in the source folder gets no outcome and no journal call.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_FrozenFichier_GetsNoOutcomeNorJournalCall_AcFr25_8()
    {
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));
        RecordingJournal failing = new(new IOException("unreachable"));
        P89FolderConverter converter = this.CappedConverter(failing);
        for (int tick = 0; tick < 3; tick++)
        {
            converter.RunTick();
        }

        Assert.Empty(converter.RunTick());
        Assert.Empty(converter.RunTick());
        Assert.Equal(3, failing.Calls);
        Assert.Single(this.folders.Names(this.folders.Source));
    }

    // AC-FR25-8: a frozen Fichier that leaves the source folder has its count dropped when its name is absent
    // from a listing, so the same name brought back by the index rotation is processed again.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_FrozenNameReused_IsProcessedAgain_AcFr25_8()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        RecordingJournal journal = new(new IOException("unreachable"));
        P89FolderConverter converter = this.CappedConverter(journal);
        for (int tick = 0; tick < 3; tick++)
        {
            converter.RunTick();
        }

        File.Delete(Path.Combine(this.folders.Source, name));
        Assert.Empty(converter.RunTick());

        journal.Failure = null;
        this.folders.Drop(name, ReadFixture(name));
        P89FichierOutcome outcome = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Converted, outcome.Status);
        Assert.Empty(this.folders.Names(this.folders.Source));
    }

    // AC-FR25-8: a Fichier converted before the cap has its count dropped at once: the same name dropped
    // again before the next listing and deferred (its done target now exists) starts from one, not Frozen.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_ConvertedBeforeTheCap_DropsTheCount_AcFr25_8()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));
        RecordingJournal journal = new(new IOException("unreachable"));
        P89FolderConverter converter = this.CappedConverter(journal);
        converter.RunTick();
        converter.RunTick();
        journal.Failure = null;
        Assert.Equal(P89FichierStatus.Converted, Assert.Single(converter.RunTick()).Status);

        this.folders.Drop(name, ReadFixture(name));

        Assert.Equal(P89FichierStatus.Deferred, Assert.Single(converter.RunTick()).Status);
    }

    // AC-FR25-8: with MaxAttempts = 1 the first deferral is already the Frozen outcome, and the next tick
    // skips the Fichier.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void RunTick_MaxAttemptsOne_FreezesAtTheFirstDeferral_AcFr25_8()
    {
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));
        RecordingJournal failing = new(new IOException("unreachable"));
        P89FolderConverter converter = this.CappedConverter(failing, 1);

        Assert.Equal(P89FichierStatus.Frozen, Assert.Single(converter.RunTick()).Status);
        Assert.Empty(converter.RunTick());
        Assert.Equal(1, failing.Calls);
    }

    // AC-FR25-8: a library MaxAttempts of zero or less keeps today's behavior: Deferred every tick.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("AC", "FR25-8")]
    public void RunTick_NoCap_DefersEveryTick_AcFr25_8(int maxAttempts)
    {
        this.folders.Drop(ReferenceFichierNames[0], ReadFixture(ReferenceFichierNames[0]));
        RecordingJournal failing = new(new IOException("unreachable"));
        P89FolderConverter converter = this.CappedConverter(failing, maxAttempts);

        for (int tick = 0; tick < 5; tick++)
        {
            Assert.Equal(P89FichierStatus.Deferred, Assert.Single(converter.RunTick()).Status);
        }

        Assert.Equal(5, failing.Calls);
    }

    // AC-FR25-8: P89:MaxAttempts defaults to 10.
    [Fact]
    [Trait("AC", "FR25-8")]
    public void P89Options_MaxAttempts_DefaultsToTen_AcFr25_8() => Assert.Equal(10, new P89Options().MaxAttempts);

    private P89FolderConverter CappedConverter(IFichierJournal journal, int maxAttempts = 3)
    {
        P89Options options = this.Options();
        options.MaxAttempts = maxAttempts;
        return new(options, journal, new FixedClock(Instant));
    }

    private P89FolderConverter Converter(DateTimeOffset instant, IFichierJournal? journal = null) =>
        new(this.Options(), journal ?? this.journal, new FixedClock(instant));

    private P89Options Options() => new()
    {
        DonePath = this.folders.Done,
        ErrorPath = this.folders.Error,
        PollingInterval = TimeSpan.FromSeconds(30),
        SourcePath = this.folders.Source,
        XmlPath = this.folders.Xml,
    };
}
