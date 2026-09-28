using System;
using System.Linq;
using System.Transactions;
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

    // AC-FR23-2: a summer instant is written in Paris summer time (UTC+2), not with a fixed offset.
    [Fact]
    [Trait("AC", "FR23-2")]
    public void Record_SummerInstant_WritesParisSummerTime_AcFr23_2()
    {
        this.Journal(InitiatingServer).Record(Entry(instant: new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new DateTime(2026, 7, 10, 10, 0, 0), Assert.Single(this.Rows()).Date);
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

    // AC-FR23-3 (F-1 of the Story 5.0 review): a success entry with an unreadable NumeroFichier and a
    // readable OF writes "<FichierName> — OK".
    [Fact]
    [Trait("AC", "FR23-3")]
    public void Record_SuccessEntryWithUnreadableNumeroFichier_WritesTheFichierNameOkRow_AcFr23_3()
    {
        this.Journal(InitiatingServer).Record(Entry(numeroFichier: null));

        Assert.Equal("LP89_682_617_013 — OK", Assert.Single(this.Rows()).Message);
    }

    // AC-FR23-3 (F-1 of the Story 5.0 review): an unreadable NumeroFichier with a readable OF (a blank
    // NumeroFichier is allowed by P89.xsd, then rejected) falls back to the FichierName, never a message
    // starting with " — ".
    [Fact]
    [Trait("AC", "FR23-3")]
    public void Record_UnreadableNumeroFichier_StartsTheMessageWithTheFichierName_AcFr23_3()
    {
        this.Journal(InitiatingServer).Record(Entry(numeroFichier: null, reasons: ["XSD : r1"]));

        Assert.Equal("LP89_682_617_013 — REJETÉ : XSD : r1", Assert.Single(this.Rows()).Message);
    }

    // AC-FR23-3 / D15: without a readable OF no row can be written.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
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

    // AC-FR23-4 / D31: the context is created with any ambient transaction suppressed, so a caller rolling
    // back its business transaction cannot take the journal row with it. Holds without a SQL Server.
    [Fact]
    [Trait("AC", "FR23-4")]
    public void Record_InsideACallerTransaction_WritesOutsideIt_AcFr23_4()
    {
        Transaction? seenByContext = null;
        AscoLsiFichierJournal journal = new(
            () =>
            {
                seenByContext = Transaction.Current;
                return this.NewContext();
            },
            InitiatingServer);

        using (new TransactionScope())
        {
            journal.Record(Entry());
        }

        Assert.Null(seenByContext);
    }

    // AC-FR24-4 (D22 revised): HasSuccess answers from the same "<NumeroFichier> — OK" rule Record writes,
    // for the entry's Commande and OF.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void HasSuccess_AfterRecordingTheSuccessEntry_IsTrue_AcFr24_4()
    {
        AscoLsiFichierJournal journal = this.Journal(InitiatingServer);
        journal.Record(Entry());

        Assert.True(journal.HasSuccess(Entry()));
    }

    // AC-FR24-4: a failure row, another OF, another Commande or another NumeroFichier is not a success of
    // this Fichier.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void HasSuccess_WithoutAMatchingOkRow_IsFalse_AcFr24_4()
    {
        AscoLsiFichierJournal journal = this.Journal(InitiatingServer);
        journal.Record(Entry(reasons: ["XSD : r1"]));
        journal.Record(Entry(of: "2039842"));
        journal.Record(Entry(numeroFichier: "014"));
        journal.Record(Entry(commande: "P60"));

        Assert.False(journal.HasSuccess(Entry()));
    }

    // AC-FR24-4 (F-1 of the Story 5.0 review): with an unreadable NumeroFichier, HasSuccess finds the
    // "<FichierName> — OK" row Record wrote, the same message head.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void HasSuccess_UnreadableNumeroFichier_MatchesTheFichierNameOkRow_AcFr24_4()
    {
        AscoLsiFichierJournal journal = this.Journal(InitiatingServer);
        journal.Record(Entry(numeroFichier: null));

        Assert.True(journal.HasSuccess(Entry(numeroFichier: " ")));
    }

    // AC-FR24-4 / D15: without a readable OF no row can exist, so there is no success to find.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void HasSuccess_UnreadableOf_IsFalse_AcFr24_4()
    {
        Assert.False(this.Journal(InitiatingServer).HasSuccess(Entry(of: " ")));
    }

    private static FichierJournal.FichierJournalEntry Entry(
        string commande = "P89",
        DateTimeOffset? instant = null,
        string? numeroFichier = "013",
        string? of = "2039841",
        string[]? reasons = null) => new()
    {
        Commande = commande,
        FichierName = "LP89_682_617_013",
        Instant = instant ?? Instant,
        NumeroFichier = numeroFichier,
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
