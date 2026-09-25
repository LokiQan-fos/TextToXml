using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using TextToXml.Tests;

namespace AscoLsiJournal.Tests;

// The LSI implementation of IFichierJournal (Story 5.0, FR-23, D31): one L_D_LOG_COMMANDE row per
// journal entry, D8 wording, D15 skip, in its own write. EF in-memory, no SQL Server. Written test-first
// (CC-1).
[Trait("Category", TestCategory.Unit)]
public class AscoLsiFichierJournalTests
{
    private const string InitiatingServer = "AFS017";

    // A winter instant: Paris is UTC+1, so the row Date reads 09:00.
    private static readonly DateTimeOffset Instant = new(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly string database = Guid.NewGuid().ToString();

    // AC-FR23-2: a success entry writes one D8 row: "<NumeroFichier> — OK", the entry's Commande and raw
    // OF, the instant in Paris time, NumLingot 0, Trace 1 and the configured initiating server.
    [Fact]
    [Trait("AC", "FR23-2")]
    public void Record_SuccessEntry_WritesTheOkRow_AcFr23_2()
    {
        this.Journal(InitiatingServer).Record(Entry());

        L_D_LOG_COMMANDE row = Assert.Single(this.Rows());
        Assert.Equal("P89", row.Commande);
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), row.Date);
        Assert.Equal("013 — OK", row.Message);
        Assert.Equal(0, row.NumLingot);
        Assert.Equal("2039841", row.OF);
        Assert.True(row.Trace);
        Assert.Equal(InitiatingServer, row.User);
    }

    // AC-FR23-2: a blank initiating server falls back to the machine name, never a blank User.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [Trait("AC", "FR23-2")]
    public void Record_BlankInitiatingServer_WritesTheMachineName_AcFr23_2(string? configured)
    {
        this.Journal(configured).Record(Entry());

        Assert.Equal(Environment.MachineName, Assert.Single(this.Rows()).User);
    }

    // AC-FR23-2: the entry's OF and the configured initiating server are written as given, like P60.
    [Fact]
    [Trait("AC", "FR23-2")]
    public void Record_ValuesAsGiven_AreWrittenUnchanged_AcFr23_2()
    {
        this.Journal(new string('S', 50)).Record(Entry(of: "0012345"));

        L_D_LOG_COMMANDE row = Assert.Single(this.Rows());
        Assert.Equal("0012345", row.OF);
        Assert.Equal(new string('S', 50), row.User);
    }

    // AC-FR23-2: an initiating server longer than L_D_LOG_COMMANDE.User fails at construction, naming the
    // setting, instead of failing on every write.
    [Fact]
    [Trait("AC", "FR23-2")]
    public void Constructor_InitiatingServerTooLong_Throws_AcFr23_2()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => this.Journal(new string('X', 51)));

        Assert.Contains("InitiatingServer", error.Message, StringComparison.Ordinal);
    }

    // AC-FR23-3: a failure entry writes "<NumeroFichier> — REJETÉ : <reasons joined by ' ; '>".
    [Fact]
    [Trait("AC", "FR23-3")]
    public void Record_FailureEntry_WritesTheRejeteRow_AcFr23_3()
    {
        this.Journal(InitiatingServer).Record(Entry(reasons: ["XSD : r1", "XSD : r2"]));

        Assert.Equal("013 — REJETÉ : XSD : r1 ; XSD : r2", Assert.Single(this.Rows()).Message);
    }

    // AC-FR23-3 / D15: without a readable OF no row can be written.
    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [Trait("AC", "FR23-3")]
    public void Record_UnreadableOf_WritesNoRow_AcFr23_3(string? of)
    {
        this.Journal(InitiatingServer).Record(Entry(of: of, reasons: ["Encodage : invalid byte"]));

        Assert.Empty(this.Rows());
    }

    // AC-FR23-4: a failing write propagates to the caller; nothing is swallowed.
    [Fact]
    [Trait("AC", "FR23-4")]
    public void Record_DatabaseUnreachable_PropagatesAndWritesNoRow_AcFr23_4()
    {
        AscoLsiFichierJournal journal = new(() => new FailingContext(this.database), InitiatingServer);

        Assert.Throws<DbUpdateException>(() => journal.Record(Entry()));
        Assert.Empty(this.Rows());
    }

    private static FichierJournal.FichierJournalEntry Entry(string? of = "2039841", string[]? reasons = null) => new()
    {
        Commande = "P89",
        FichierName = "LP89_682_617_013",
        Instant = Instant,
        NumeroFichier = "013",
        OF = of,
        Reasons = reasons ?? [],
    };

    private AscoLsiFichierJournal Journal(string? initiatingServer) => new(this.NewContext, initiatingServer);

    private AscoLsiJournalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AscoLsiJournalDbContext>().UseInMemoryDatabase(this.database).Options);

    private L_D_LOG_COMMANDE[] Rows()
    {
        using AscoLsiJournalDbContext context = this.NewContext();
        return [.. context.LogCommandeRows.AsNoTracking()];
    }

    // A context over the test's own in-memory database whose SaveChanges fails the way an unreachable
    // database does.
    private sealed class FailingContext(string database)
        : AscoLsiJournalDbContext(new DbContextOptionsBuilder<AscoLsiJournalDbContext>().UseInMemoryDatabase(database).Options)
    {
        public override int SaveChanges() => throw new DbUpdateException("database unreachable");
    }
}
