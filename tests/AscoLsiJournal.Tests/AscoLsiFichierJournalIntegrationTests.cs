using System;
using System.Linq;
using System.Transactions;
using Kape22Importer.Tests;
using Microsoft.EntityFrameworkCore;
using TextToXml.Tests;

namespace AscoLsiJournal.Tests;

// The real L_D_LOG_COMMANDE writes against the local SQL Server test instance (AR-12): the rows the
// in-memory tests describe must satisfy the real table, and Record must commit on its own, even inside a
// caller's transaction that rolls back (D31). Commit + reset regime. Skips cleanly when no instance is
// configured. Written test-first (CC-1).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class AscoLsiFichierJournalIntegrationTests(SqlServerIntegrationFixture fixture)
{
    // AC-FR23-4: when Record returns, the row is committed and visible from a fresh context, every column
    // included; the accented REJETÉ wording round-trips through the real column.
    [SkippableFact]
    [Trait("AC", "FR23-4")]
    public void Record_WithoutAmbientTransaction_CommitsTheRow_AcFr23_4()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        fixture.ResetData();

        this.Journal().Record(Entry(reasons: ["XSD : élément inattendu"]));

        using AscoLsiJournalDbContext verify = this.NewContext();
        L_D_LOG_COMMANDE row = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal("P89", row.Commande);
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), row.Date);
        Assert.Equal("013 — REJETÉ : XSD : élément inattendu", row.Message);
        Assert.Equal(0, row.NumLingot);
        Assert.Equal("2039841", row.OF);
        Assert.True(row.Trace);
        Assert.Equal("AFS017", row.User);
    }

    // AC-FR23-4 / D31: a caller's business transaction that rolls back does not take the journal row with
    // it; the journal records the result of the import, whatever that result is.
    [SkippableFact]
    [Trait("AC", "FR23-4")]
    public void Record_InsideARolledBackCallerTransaction_KeepsTheRow_AcFr23_4()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason);
        fixture.ResetData();

        using (new TransactionScope())
        {
            this.Journal().Record(Entry());

            // Disposed without Complete(): the caller's transaction rolls back.
        }

        using AscoLsiJournalDbContext verify = this.NewContext();
        Assert.Equal("013 — OK", Assert.Single(verify.LogCommandeRows.AsNoTracking()).Message);
    }

    private static FichierJournal.FichierJournalEntry Entry(string[]? reasons = null) => new()
    {
        Commande = "P89",
        FichierName = "LP89_682_617_013",
        // A winter instant: Paris is UTC+1, so the row Date reads 09:00.
        Instant = new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.Zero),
        NumeroFichier = "013",
        OF = "2039841",
        Reasons = reasons ?? [],
    };

    private AscoLsiFichierJournal Journal() => new(this.NewContext, "AFS017");

    private AscoLsiJournalDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AscoLsiJournalDbContext>().UseSqlServer(fixture.AscoLsiConnectionString).Options);
}
