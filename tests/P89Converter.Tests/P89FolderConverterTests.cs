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
            this.Options(), this.journal, new FixedClock(Instant), P89FichierConverterTests.StricterSchema());
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
            P89FichierConverterTests.StricterSchema());
        P89FichierOutcome outcome = Assert.Single(converter.RunTick());

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
        Assert.Contains(outcome.Reasons, reason => reason.StartsWith("XSD", StringComparison.Ordinal));
        Assert.Contains(outcome.Reasons, reason => reason.StartsWith("Journal", StringComparison.Ordinal));
        Assert.Equal([AccentedFichierName], this.folders.Names(this.folders.Source));
        Assert.Empty(this.folders.Names(this.folders.Error));
    }

    // A file-system fault (source locked, unreadable) defers the Fichier without any entry.
    [Fact]
    public void RunTick_SourceUnreadable_DefersWithoutEntry()
    {
        string name = ReferenceFichierNames[0];
        this.folders.Drop(name, ReadFixture(name));

        P89FichierOutcome outcome;
        using (new FileStream(Path.Combine(this.folders.Source, name), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = Assert.Single(this.Converter(Instant).RunTick());
        }

        Assert.Equal(P89FichierStatus.Deferred, outcome.Status);
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
