using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 2.8 (FR-11): Kape22Persister turns a MapResult into AscoLSI rows - atomic L_D_KAPE22 +
// L_D_LOG_COMMANDE insert on success, a lone REJETÉ log row on a rejection with a readable OF, a
// verified rollback and a PersistenceError (never an exception) on a SQL failure, and the D22
// anti-duplicate guard in front of all of it. Written test-first (CC-1): red until Kape22Persister
// ships. Integration category (AR-12): needs a reachable local SQL Server test instance, skips cleanly
// otherwise. Every test runs in the commit + reset regime (ResetData first), never under an ambient
// TransactionScope, because the transaction boundaries themselves are under test (AC-FR11-3/11-5) and
// the guard needs committed state (AC-FR11-6/11-7). Vocabulary follows the PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class TransactionalPersistenceTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    // AC-FR11-1: MapResult.Success == true -> exactly one L_D_KAPE22 row, and ImportResult.InsertedId is
    // the generated identity value of that row.
    [SkippableFact]
    [Trait("AC", "FR11-1")]
    public void Persist_MapSuccess_InsertsOneKape22RowWithInsertedId_AcFr11_1()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        ImportResult result = Persist(map);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_KAPE22 inserted = Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.Equal(inserted.Id, result.InsertedId);
    }

    // AC-FR11-2: MapResult.Success == false -> no L_D_KAPE22 row, InsertedId == null.
    [SkippableFact]
    [Trait("AC", "FR11-2")]
    public void Persist_MapFailure_InsertsNoKape22RowAndInsertedIdIsNull_AcFr11_2()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapMutatedFichier(d => SetChamp(d, "message", "Client", string.Empty));
        Assert.False(map.Success);

        ImportResult result = Persist(map);

        Assert.False(result.Success);
        Assert.Null(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
    }

    // AC-FR11-3: on success the L_D_KAPE22 insert and the "<NumeroFichier> — OK" L_D_LOG_COMMANDE insert
    // are committed together - both rows are present afterwards.
    [SkippableFact]
    [Trait("AC", "FR11-3")]
    public void Persist_MapSuccess_CommitsKape22AndOkLogRowTogether_AcFr11_3()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        Persist(map);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.StartsWith(map.NumeroFichier!, log.Message);
        Assert.EndsWith("— OK", log.Message);

        // The log Date is the injected clock converted to Paris local time (WinterClock is 08:00 UTC,
        // Paris winter UTC+1), so the timeProvider seam and the conversion both have coverage.
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), log.Date);
    }

    // AC-FR14-4 (D8, D25): the "— OK" L_D_LOG_COMMANDE row carries the fixed contract fields - User from
    // Import:InitiatingServer, OF the trimmed raw Detail Champ (the Kape22Mapper already trims it),
    // Commande "P60", NumLingot 0 and Trace true.
    [SkippableFact]
    [Trait("AC", "FR14-4")]
    public void Persist_MapSuccess_OkLogRowCarriesTheContractFields_AcFr14_4()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        Persist(map);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal("P60", log.Commande);
        Assert.Equal(InitiatingServer, log.User);
        Assert.Equal(map.OF, log.OF);
        Assert.Equal(log.OF.Trim(), log.OF);
        Assert.Equal(0, log.NumLingot);
        Assert.True(log.Trace == true);
    }

    // AC-FR11-3: when the L_D_LOG_COMMANDE insert fails (an over-long Commande from configuration), the
    // L_D_KAPE22 insert of the same transaction is rolled back too - the entity itself is valid.
    [SkippableFact]
    [Trait("AC", "FR11-3")]
    public void Persist_MapSuccess_LogRowInsertFails_RollsBackKape22Row_AcFr11_3()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        ImportResult result = Persist(map, commande: new string('P', 100));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
    }

    // AC-FR11-4: a rejected Fichier with a readable OF -> no L_D_KAPE22 row, exactly one
    // "<NumeroFichier> — REJETÉ : <summary>" L_D_LOG_COMMANDE row carrying the OF.
    [SkippableFact]
    [Trait("AC", "FR11-4")]
    public void Persist_MapFailure_WithReadableOf_WritesOneRejectedLogRow_AcFr11_4()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapMutatedFichier(d => SetChamp(d, "message", "Client", string.Empty));
        Assert.False(map.Success);
        Assert.False(string.IsNullOrWhiteSpace(map.OF));

        ImportResult result = Persist(map);

        Assert.False(result.Success);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal("P60", log.Commande);
        Assert.StartsWith(map.NumeroFichier!, log.Message);
        Assert.Contains("REJETÉ", log.Message);
        Assert.Equal(map.OF!.Trim(), log.OF.Trim());
    }

    // AC-FR11-5: a SQL failure on the L_D_KAPE22 insert (Client over its bounded column) comes back as
    // {Block:File, Code:PersistenceError} with no exception escaping, and nothing is left committed.
    [SkippableFact]
    [Trait("AC", "FR11-5")]
    public void Persist_SqlFailure_ReturnsPersistenceErrorWithVerifiedRollback_AcFr11_5()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapMutatedFichier(d => SetChamp(d, "message", "Client", new string('A', 50)));
        Assert.True(map.Success, "over-long Client is only rejected by the database, not by the mapper.");

        ImportResult result = Persist(map);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors, e => e.Code == ErrorCode.PersistenceError);
        Assert.Equal(Block.File, error.Block);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
    }

    // AC-FR11-6 (D22): a prior "<NumeroFichier> — OK" L_D_LOG_COMMANDE row for the same NumeroFichier +
    // OF makes the persister skip - no new L_D_KAPE22 row, InsertedId null, Success stays true (the
    // Fichier is fine, just already imported), Errors empty. That shape is what the orchestrator reads
    // to archive the Fichier and log the "deja importe" warning.
    [SkippableFact]
    [Trait("AC", "FR11-6")]
    public void Persist_PriorOkLogRowForSameKey_SkipsInsert_AcFr11_6()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();
        SeedOkLogRow(map.NumeroFichier!, map.OF!);

        ImportResult result = Persist(map);

        Assert.True(result.Success);
        Assert.Null(result.InsertedId);
        Assert.Empty(result.Errors);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Single(verify.LogCommandeRows.AsNoTracking());
    }

    // AC-FR11-6: the guard keys on exactly what BuildLogRow writes - persisting the same Fichier twice
    // skips the second time, with no hard-coded message string in the assertion.
    [SkippableFact]
    [Trait("AC", "FR11-6")]
    public void Persist_SameFichierTwice_SecondCallSkips_AcFr11_6()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        ImportResult first = Persist(map);
        ImportResult second = Persist(map);

        Assert.NotNull(first.InsertedId);
        Assert.True(second.Success);
        Assert.Null(second.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
    }

    // AC-FR11-7: a Fichier that was never imported successfully (only a REJETÉ row exists for the key)
    // imports normally.
    [SkippableFact]
    [Trait("AC", "FR11-7")]
    public void Persist_OnlyRejectedLogRowForKey_ImportsNormally_AcFr11_7()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();
        SeedLogRow(map.NumeroFichier!, map.OF!, $"{map.NumeroFichier} — REJETÉ : 1 erreur");

        ImportResult result = Persist(map);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
    }

    // AC-FR11-8: the persister never carries a hard-coded connection string; the DbContext it writes
    // through gets its connection from configuration (CC-7). Covered without a database in
    // PersisterConfigurationTests; this marker keeps the AC visible in the Integration view.
    [SkippableFact]
    [Trait("AC", "FR11-8")]
    public void Persist_UsesConfiguredConnection_AcFr11_8()
    {
        Ready();
        MapResult<L_D_KAPE22> map = MapReferenceFichier();

        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        Assert.Contains("AscoLSI_Test", context.Database.GetConnectionString() ?? string.Empty);

        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(map);
        Assert.True(result.Success);
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
    }

    private ImportResult Persist(MapResult<L_D_KAPE22> map, string? initiatingServer = null, string? commande = null)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        return new Kape22Persister(context, Configuration(initiatingServer ?? InitiatingServer, commande), WinterClock()).Persist(map);
    }

    private static IConfiguration Configuration(string initiatingServer = InitiatingServer, string? commande = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = commande ?? "P60",
                ["Import:InitiatingServer"] = initiatingServer,
            })
            .Build();

    private void SeedOkLogRow(string numeroFichier, string of) =>
        SeedLogRow(numeroFichier, of, $"{numeroFichier} — OK");

    private void SeedLogRow(string numeroFichier, string of, string message)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.LogCommandeRows.Add(new L_D_LOG_COMMANDE
        {
            Commande = "P60",
            Date = new DateTime(2026, 2, 9, 12, 0, 0),
            Message = message,
            NumLingot = 0,
            OF = of.Trim(),
            Trace = true,
            User = InitiatingServer,
        });
        context.SaveChanges();
    }
}
