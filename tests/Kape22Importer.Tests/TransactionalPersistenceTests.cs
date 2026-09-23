using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 2.8 (FR-11), extended by Story 4.6 (FR-21): Kape22Persister turns a Kape22ImportBundle into
// AscoLSI rows - a single-transaction L_D_KAPE22 + L_D_LOG_COMMANDE + every non-null Story 4.1 downstream
// entity insert on success, a lone REJETÉ log row on a rejection with a readable OF, a verified rollback
// and a PersistenceError (never an exception) on a SQL failure, the D22 anti-duplicate guard in front of
// all of it, and the AC-FR20-5 cold-Coulee existence guard between the two. Written test-first (CC-1):
// red until Kape22Persister ships. Integration category (AR-12): needs a reachable local SQL Server test
// instance, skips cleanly otherwise. Every test runs in the commit + reset regime (ResetData first),
// never under an ambient TransactionScope, because the transaction boundaries themselves are under test
// (AC-FR11-3/11-5/21-1/21-2) and the guard needs committed state (AC-FR11-6/11-7). Vocabulary follows the
// PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class TransactionalPersistenceTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    // AC-FR11-1: bundle.Success == true -> exactly one L_D_KAPE22 row, and ImportResult.InsertedId is
    // the generated identity value of that row.
    [SkippableFact]
    [Trait("AC", "FR11-1")]
    public void Persist_BundleSuccess_InsertsOneKape22RowWithInsertedId_AcFr11_1()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();

        ImportResult result = Persist(bundle);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_KAPE22 inserted = Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.Equal(inserted.Id, result.InsertedId);
    }

    // AC-FR11-2: bundle.Success == false -> no L_D_KAPE22 row, InsertedId == null.
    [SkippableFact]
    [Trait("AC", "FR11-2")]
    public void Persist_BundleFailure_InsertsNoKape22RowAndInsertedIdIsNull_AcFr11_2()
    {
        Ready();
        Kape22ImportBundle bundle = MapMutatedBundle(d => SetChamp(d, "message", "Client", string.Empty));
        Assert.False(bundle.Success);

        ImportResult result = Persist(bundle);

        Assert.False(result.Success);
        Assert.Null(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
    }

    // AC-FR11-3: on success the L_D_KAPE22 insert and the "<NumeroFichier> — OK" L_D_LOG_COMMANDE insert
    // are committed together - both rows are present afterwards.
    [SkippableFact]
    [Trait("AC", "FR11-3")]
    public void Persist_BundleSuccess_CommitsKape22AndOkLogRowTogether_AcFr11_3()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();

        Persist(bundle);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.StartsWith(bundle.NumeroFichier!, log.Message);
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
    public void Persist_BundleSuccess_OkLogRowCarriesTheContractFields_AcFr14_4()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();

        Persist(bundle);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal("P60", log.Commande);
        Assert.Equal(InitiatingServer, log.User);
        Assert.Equal(bundle.OF, log.OF);
        Assert.Equal(log.OF.Trim(), log.OF);
        Assert.Equal(0, log.NumLingot);
        Assert.True(log.Trace == true);
    }

    // A real deployment's GpaoImportP60.json ships with an empty "InitiatingServer" string, not an
    // absent key - a bare `??` in Kape22Persister.ResolveUser never caught that, silently leaving User
    // blank on every row (found 2026-09-22 by inspecting a real e2e run's L_D_LOG_COMMANDE). Falls back
    // to the machine name on blank/whitespace, the same as a genuinely absent setting.
    [SkippableFact]
    [Trait("AC", "FR11-8")]
    public void Persist_BlankInitiatingServer_FallsBackToMachineName_AcFr11_8()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();

        Persist(bundle, initiatingServer: "");

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal(Environment.MachineName, log.User.Trim());
    }

    // AC-FR11-3: when the L_D_LOG_COMMANDE insert fails (an over-long Commande from configuration), the
    // L_D_KAPE22 insert of the same transaction is rolled back too - the entity itself is valid.
    [SkippableFact]
    [Trait("AC", "FR11-3")]
    public void Persist_BundleSuccess_LogRowInsertFails_RollsBackKape22Row_AcFr11_3()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();

        ImportResult result = Persist(bundle, commande: new string('P', 100));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());

        // AC-FR21-1's atomicity cuts both ways here too: the rolled-back transaction also staged every
        // downstream entity, so none of the other 9 tables kept a row either (mirrors AC-FR20-5's check).
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking());
        Assert.Empty(verify.CouleeRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking());
        Assert.Empty(verify.ConsignesRows.AsNoTracking());
    }

    // AC-FR11-4: a rejected Fichier with a readable OF -> no L_D_KAPE22 row, exactly one
    // "<NumeroFichier> — REJETÉ : <summary>" L_D_LOG_COMMANDE row carrying the OF.
    [SkippableFact]
    [Trait("AC", "FR11-4")]
    public void Persist_BundleFailure_WithReadableOf_WritesOneRejectedLogRow_AcFr11_4()
    {
        Ready();
        Kape22ImportBundle bundle = MapMutatedBundle(d => SetChamp(d, "message", "Client", string.Empty));
        Assert.False(bundle.Success);
        Assert.False(string.IsNullOrWhiteSpace(bundle.OF));

        ImportResult result = Persist(bundle);

        Assert.False(result.Success);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Equal("P60", log.Commande);
        Assert.StartsWith(bundle.NumeroFichier!, log.Message);
        Assert.Contains("REJETÉ", log.Message);
        Assert.Equal(bundle.OF!.Trim(), log.OF.Trim());
    }

    // AC-FR11-5 / AC-FR21-2: a SQL failure on the L_D_KAPE22 insert comes back as
    // {Block:File, Code:PersistenceError} with no exception escaping, and none of the rows staged for
    // that one SaveChanges is left committed - not just L_D_KAPE22, every downstream entity too
    // (AC-FR21-2 extends AC-FR11-5). LibelleConsigneChutage is over-long here rather than the once-used
    // Client: Story 4.11's C-4 pre-check now catches an over-long Client before SaveChanges even runs
    // (Client is also L_D_ORDRE_FABRICATION.Client, a DownstreamColumnLengths-bounded column), so this
    // test needs a bounded L_D_KAPE22-only column that no downstream mapper copies anywhere - a genuine,
    // undiagnosed SQL truncation is still reachable there.
    [SkippableFact]
    [Trait("AC", "FR11-5")]
    [Trait("AC", "FR21-2")]
    public void Persist_SqlFailure_ReturnsPersistenceErrorWithVerifiedRollback_AcFr11_5()
    {
        Ready();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "LibelleConsigneChutage", new string('A', 50));
        });
        Assert.True(bundle.Success, "over-long LibelleConsigneChutage is only rejected by the database, not by the mapper.");

        ImportResult result = Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors, e => e.Code == ErrorCode.PersistenceError);
        Assert.Equal(Block.File, error.Block);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.CouleeRows.AsNoTracking().Where(row => row.IdCoulee.Trim() == bundle.Kape22!.Coulee.Trim()));
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.ConsignesRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
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
        Kape22ImportBundle bundle = MapReferenceBundle();
        SeedOkLogRow(bundle.NumeroFichier!, bundle.OF!);

        ImportResult result = Persist(bundle);

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
        Kape22ImportBundle bundle = MapReferenceBundle();

        ImportResult first = Persist(bundle);
        ImportResult second = Persist(bundle);

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
        Kape22ImportBundle bundle = MapReferenceBundle();
        SeedLogRow(bundle.NumeroFichier!, bundle.OF!, $"{bundle.NumeroFichier} — REJETÉ : 1 erreur");

        ImportResult result = Persist(bundle);

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
        Kape22ImportBundle bundle = MapReferenceBundle();

        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        Assert.Contains("AscoLSI_Test", context.Database.GetConnectionString() ?? string.Empty);

        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);
        Assert.True(result.Success);
    }

    // AC-FR20-5: a cold Coulee (CodeConsignePits == "1") whose L_D_COULEE row does not exist yet is
    // rejected before anything is added - no L_D_KAPE22 row, no downstream entity, one REJETÉ log row
    // citing the missing Coulee, and a BusinessRuleViolation error naming it.
    [SkippableFact]
    [Trait("AC", "FR20-5")]
    public void Persist_ColdCouleeMissingFromLDCoulee_RejectsWithNoInsertsAndBusinessRuleViolation_AcFr20_5()
    {
        Ready();
        Kape22ImportBundle bundle = MapMutatedBundle(d => SetChamp(d, "message", "CodeConsignePits", "1"));
        Assert.True(bundle.Success, "the cold happy-path fixture must still pass every FR-20 control.");
        string coulee = bundle.Kape22!.Coulee;

        using (AscoLsiDbContext precondition = fixture.NewAscoLsiContext())
        {
            Assert.False(
                precondition.CouleeRows.AsNoTracking().Any(row => row.IdCoulee == coulee),
                $"test setup: L_D_COULEE must not already carry '{coulee}'.");
        }

        ImportResult result = Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(coulee, error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.CouleeRows.AsNoTracking().Where(row => row.IdCoulee == coulee));
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);
        Assert.Contains(coulee, log.Message, StringComparison.Ordinal);

        // AC-FR21-1's atomicity cuts both ways: a cold-Coulee rejection must leave every downstream
        // table empty too, not just L_D_KAPE22/L_D_COULEE.
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.ConsignesRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
    }

    // AC-FR20-5 / AC-FR21-1: a cold Coulee (CodeConsignePits == "1") whose L_D_COULEE row already exists
    // is the spec's own described "expected, normal" case (several OF from the same cast) - it must
    // succeed, not be rejected, and must not re-insert the already-present Coulee row. A mutation check
    // confirms this is the guard the previous review left untested: narrowing Kape22Persister.cs's cold
    // guard from `CodeConsignePits == ColdConsignePits && !couleeAlreadyExists` to just
    // `CodeConsignePits == ColdConsignePits` (rejecting every cold order unconditionally) passed the full
    // suite until this test was added.
    [SkippableFact]
    [Trait("AC", "FR20-5")]
    [Trait("AC", "FR21-1")]
    public void Persist_ColdCouleeAlreadyOnFile_SucceedsWithoutDuplicatingTheCouleeRow_AcFr20_5()
    {
        Ready();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "CodeConsignePits", "1");
        });
        Assert.True(bundle.Success, "the cold happy-path fixture must still pass every FR-20 control.");
        string coulee = bundle.Kape22!.Coulee;

        using (AscoLsiDbContext seed = fixture.NewAscoLsiContext())
        {
            seed.CouleeRows.Add(bundle.Coulee!);
            seed.SaveChanges();
        }

        ImportResult result = Persist(bundle);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.Single(verify.CouleeRows.AsNoTracking().Where(row => row.IdCoulee.Trim() == coulee.Trim()));
    }

    // C-9 (Épic 4 retro #3): the test above seeds the Coulee row directly through EF, proving only the
    // read side of the guard. This one closes the gap it documents: two genuine, sequential Persist calls
    // on two separate DbContext instances (the shared Persist helper opens a fresh
    // fixture.NewAscoLsiContext() per call), dispatching two different OFs of the same hot Coulee. The
    // second dispatch must reuse the Coulee row the first one created - not duplicate it, and not
    // overwrite it with the second bundle's own (deliberately different) Nuance.
    [SkippableFact]
    [Trait("AC", "C-9")]
    public void Persist_TwoRealDispatchesShareOneHotCoulee_SecondReusesWithoutModifyingTheFirstsCouleeRow_AcC9()
    {
        Ready();
        Kape22ImportBundle first = MapReferenceBundle();
        string coulee = first.Kape22!.Coulee;
        string firstOf = first.OF!;
        string firstNuance = first.Coulee!.Nuance;
        string secondOf = firstOf[..^1] + (firstOf[^1] == '9' ? '8' : '9');
        string secondNuance = firstNuance == "ACIERX" ? "ACIERY" : "ACIERX";

        Kape22ImportBundle second = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "OF", secondOf);
            SetChamp(d, "message", "Nuance", secondNuance);
        });
        Assert.True(second.Success, "the OF/Nuance-only mutation must not trip any FR-20 control.");
        Assert.Equal(coulee, second.Kape22!.Coulee);
        Assert.NotEqual(firstNuance, second.Coulee!.Nuance);

        ImportResult firstResult = Persist(first);
        ImportResult secondResult = Persist(second);

        Assert.True(firstResult.Success, string.Join("; ", firstResult.Errors.Select(error => error.Message)));
        Assert.True(secondResult.Success, string.Join("; ", secondResult.Errors.Select(error => error.Message)));

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Equal(2, verify.Kape22Rows.AsNoTracking().Count(row => row.Coulee.Trim() == coulee.Trim()));
        L_D_COULEE couleeRow = Assert.Single(
            verify.CouleeRows.AsNoTracking().Where(row => row.IdCoulee.Trim() == coulee.Trim()));

        // Nuance is a fixed-width CHAR column, so a real round-trip pads the read-back value.
        Assert.Equal(firstNuance, couleeRow.Nuance.Trim());
    }

    // AC-FR21-1 (AD-1): a bundle that passes the guard with a hot Coulee (the existence check skipped)
    // commits L_D_KAPE22 + the OK log row + every non-null downstream entity from the bundle - Story
    // 4.3/4.4's OrdreFabrication, Coulee and per-OF-applicable SectionCharge*/Consignes - in one
    // SaveChanges.
    [SkippableFact]
    [Trait("AC", "FR21-1")]
    public void Persist_FullSuccess_CommitsKape22AndEveryDownstreamEntityInOneSaveChanges_AcFr21_1()
    {
        Ready();
        Kape22ImportBundle bundle = MapReferenceBundle();
        Assert.True(bundle.Success);

        ImportResult result = Persist(bundle);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Single(verify.OrdreFabricationRows.AsNoTracking(), row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!));
        Assert.Single(verify.CouleeRows.AsNoTracking(), row => row.IdCoulee.Trim() == bundle.Kape22!.Coulee.Trim());
        Assert.Equal(
            bundle.SectionChargeChutage is null ? 0 : 1,
            verify.SectionChargeChutageRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargeDecoupe is null ? 0 : 1,
            verify.SectionChargeDecoupeRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargeLingot is null ? 0 : 1,
            verify.SectionChargeLingotRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargePits is null ? 0 : 1,
            verify.SectionChargePitsRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargePoidsMetrique is null ? 0 : 1,
            verify.SectionChargePoidsMetriqueRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargeRefroidissoirs is null ? 0 : 1,
            verify.SectionChargeRefroidissoirsRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.SectionChargeSvt is null ? 0 : 1,
            verify.SectionChargeSvtRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Equal(
            bundle.Consignes.Count,
            verify.ConsignesRows.AsNoTracking().Count(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
    }

    // A-5 (Epic 4 retro): a same-bundle L_D_CONSIGNES natural-key collision - two sections sharing the
    // same CodeOperation for one OF - is caught by an in-memory pre-check before AddRange, instead of
    // throwing an uncaught InvalidOperationException at that call. Forcing CodeOpeDecoupe to the
    // Chutage section's own CodeOpeChutage value (both sections stay applicable, only the shared
    // discriminant collides) reproduces the collision without touching TypeConsigne/ConsigneGPAO,
    // exactly the scenario ConsignesMapper.cs's own comment documents as unguarded today. Modeled on
    // Persist_ColdCouleeMissingFromLDCoulee_RejectsWithNoInsertsAndBusinessRuleViolation_AcFr20_5's
    // rejection shape: one ConversionError (BusinessRuleViolation), one REJETÉ log row, zero rows in
    // every one of the 10 downstream tables.
    [SkippableFact]
    [Trait("AC", "A-5")]
    public void Persist_ConsignesNaturalKeyCollision_RejectsWithNoInsertsAndBusinessRuleViolation_A5()
    {
        Ready();
        Kape22ImportBundle bundle = ConsignesCollisionBundle();
        Assert.True(bundle.Success, "the collision must be caught by the pre-check, not by an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeChutage);
        Assert.NotNull(bundle.SectionChargeDecoupe);
        Assert.Equal(bundle.SectionChargeChutage!.CodeOperation, bundle.SectionChargeDecoupe!.CodeOperation);

        ImportResult? result = null;
        Exception? exception = Record.Exception(() => result = Persist(bundle));
        Assert.Null(exception);

        Assert.False(result!.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.SectionChargeChutage!.CodeOperation, error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);

        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.CouleeRows.AsNoTracking().Where(row => row.IdCoulee.Trim() == bundle.Kape22!.Coulee.Trim()));
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
        Assert.Empty(verify.ConsignesRows.AsNoTracking().Where(row => row.OF.Trim() == DownstreamOf.Pad(bundle.OF!)));
    }

    // A-5 (Epic 4 retro): the collision branch's own REJETÉ-log SaveChanges can itself fail at the DB
    // level (an over-long Commande, same forcing technique as
    // Persist_BundleSuccess_LogRowInsertFails_RollsBackKape22Row_AcFr11_3) - nothing exercised that
    // nested catch before this test (verification-gap review finding). Persist must still come back as
    // an ImportResult - never throw - carrying both the original BusinessRuleViolation collision error
    // and the PersistenceError, with every table left empty.
    [SkippableFact]
    [Trait("AC", "A-5")]
    public void Persist_ConsignesNaturalKeyCollision_LogRowInsertAlsoFails_ReturnsBothErrorsWithNoInserts_A5()
    {
        Ready();
        Kape22ImportBundle bundle = ConsignesCollisionBundle();
        Assert.True(bundle.Success, "the collision must be caught by the pre-check, not by an upstream FR-20 control.");

        ImportResult? result = null;
        Exception? exception = Record.Exception(() => result = Persist(bundle, commande: new string('P', 100)));
        Assert.Null(exception);

        Assert.False(result!.Success);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking());
        Assert.Empty(verify.CouleeRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking());
        Assert.Empty(verify.ConsignesRows.AsNoTracking());
    }

    // C-6 (Épic 4 retro #3): the Matrix's "Simultaneous A-5 + B-5 violation" row - a bundle failing both
    // the Consignes-collision (A-5) and magnitude-overflow (B-5) pre-checks at once must come back as one
    // REJETÉ log row / ConversionError naming both causes, not two sequential ones. Reuses
    // ConsignesCollisionBundle's own CodeOpeDecoupe-onto-CodeOpeChutage forcing technique for A-5, adding
    // an out-of-gabarit ChutageTete (same DECIMAL(3,2)/bound-10 shape C-1 already covers) for B-5.
    // Category=Unit (AR-12, EF InMemory provider): both pre-checks are pure in-memory computation before
    // SaveChanges, so no real SQL Server round-trip is needed. Written test-first (CC-1).
    [Fact]
    [Trait("Category", TestCategory.Unit)]
    [Trait("AC", "C-6")]
    public void Persist_SimultaneousConsignesCollisionAndMagnitudeOverflow_RejectsWithOneMessageNamingBothCauses_AcC6()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            string codeOpeChutage = d.Root!.Element("message")!.Element("CodeOpeChutage")!.Value;
            SetChamp(d, "message", "CodeOpeDecoupe", codeOpeChutage);
            SetChamp(d, "message", "ChutageTete", "99999");
        });
        Assert.True(bundle.Success, "the mutations must only trip the A-5/B-5 pre-checks, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeChutage);
        Assert.NotNull(bundle.SectionChargeDecoupe);
        Assert.Equal(
            bundle.SectionChargeChutage!.CodeOperation, bundle.SectionChargeDecoupe!.CodeOperation, StringComparer.Ordinal);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);

        // C-6: one accumulated ConversionError, not two sequential ones.
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.SectionChargeChutage!.CodeOperation, error.Message, StringComparison.Ordinal);
        Assert.Contains("ChutageTete", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows);
        Assert.Contains("REJETÉ", log.Message);
        Assert.Empty(verify.SectionChargeChutageRows);
        Assert.Empty(verify.SectionChargeDecoupeRows);
        Assert.Empty(verify.ConsignesRows);
    }

    // C-6 code-review patch (Épic 4 retro #3): C-4 originally sat after the A-5/B-5 accumulation as its
    // own separate early return, silently reintroducing the "only report whichever fired first" symptom
    // C-6 exists to eliminate - one guard later, undocumented. This proves C-4 now accumulates alongside
    // A-5/B-5 too: a bundle failing both the Consignes-collision (A-5) and length-overflow (C-4)
    // pre-checks at once must come back as one REJETÉ log row / ConversionError naming both causes.
    // Category=Unit (AR-12, EF InMemory provider): both pre-checks are pure in-memory computation before
    // SaveChanges, so no real SQL Server round-trip is needed. Written test-first (CC-1).
    [Fact]
    [Trait("Category", TestCategory.Unit)]
    [Trait("AC", "C-6")]
    public void Persist_SimultaneousConsignesCollisionAndLengthOverflow_RejectsWithOneMessageNamingBothCauses_AcC6()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            string codeOpeChutage = d.Root!.Element("message")!.Element("CodeOpeChutage")!.Value;
            SetChamp(d, "message", "CodeOpeDecoupe", codeOpeChutage);
            SetChamp(d, "message", "MarqueCommerciale", new string('A', 20));
        });
        Assert.True(bundle.Success, "the mutations must only trip the A-5/C-4 pre-checks, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeChutage);
        Assert.NotNull(bundle.SectionChargeDecoupe);
        Assert.Equal(
            bundle.SectionChargeChutage!.CodeOperation, bundle.SectionChargeDecoupe!.CodeOperation, StringComparer.Ordinal);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);

        // C-6: one accumulated ConversionError, not two sequential ones.
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.SectionChargeChutage!.CodeOperation, error.Message, StringComparison.Ordinal);
        Assert.Contains("MarqueCommerciale", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.OrdreFabricationRows);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows);
        Assert.Contains("REJETÉ", log.Message);
        Assert.Empty(verify.SectionChargeChutageRows);
        Assert.Empty(verify.SectionChargeDecoupeRows);
        Assert.Empty(verify.ConsignesRows);
    }

    // A-4 (Epic 4 retro) non-regression: Kape22Persister and Kape22ImportBundleMapper both read the one
    // shared Kape22ImportBundle.ColdConsignePits constant for the hot/cold Coulee marker, instead of each
    // carrying its own "1" literal - pinned by reflection so a future revert back to a private duplicate
    // fails this test instead of silently reintroducing the drift risk the retro flagged. Pure
    // reflection, no database needed.
    [Fact]
    [Trait("AC", "A-4")]
    public void ColdConsignePits_IsTheOneSharedConstantBothCollaboratorsReference_A4()
    {
        Assert.Equal("1", Kape22ImportBundle.ColdConsignePits);

        FieldInfo? persisterOwnConstant = typeof(Kape22Persister)
            .GetField("ColdConsignePits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.Null(persisterOwnConstant);

        FieldInfo? bundleMapperOwnConstant = typeof(Kape22ImportBundleMapper)
            .GetField("ColdConsignePits", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        Assert.Null(bundleMapperOwnConstant);
    }

    // AC-FR21-3 (AD-6): Kape22Persister exposes exactly one Persist overload, taking a
    // Kape22ImportBundle - the MapResult<L_D_KAPE22> overload this story replaces is gone. Pure
    // reflection, no database needed.
    [Fact]
    [Trait("AC", "FR21-3")]
    public void Kape22Persister_ExposesOnlyTheBundlePersistOverload_AcFr21_3()
    {
        MethodInfo[] persistMethods = typeof(Kape22Persister)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(Kape22Persister.Persist))
            .ToArray();

        MethodInfo method = Assert.Single(persistMethods);
        ParameterInfo parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(Kape22ImportBundle), parameter.ParameterType);
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
    }

    private ImportResult Persist(Kape22ImportBundle bundle, string? initiatingServer = null, string? commande = null)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        return new Kape22Persister(context, Configuration(initiatingServer ?? InitiatingServer, commande), WinterClock()).Persist(bundle);
    }

    private static IConfiguration Configuration(string initiatingServer = InitiatingServer, string? commande = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = commande ?? "P60",
                ["Import:InitiatingServer"] = initiatingServer,
            })
            .Build();

    // A-5 (Epic 4 retro): the reference bundle mutated so CodeOpeDecoupe collides with CodeOpeChutage -
    // shared by both A-5 tests above (the plain rejection and the log-row-insert-also-fails variant).
    private static Kape22ImportBundle ConsignesCollisionBundle() =>
        MapMutatedBundle(d =>
        {
            // Coulee kept at the conventional internal value used throughout this file's fixtures, so
            // only the CodeOperation collision this test targets trips a control.
            SetChamp(d, "message", "Coulee", "065718");
            string codeOpeChutage = d.Root!.Element("message")!.Element("CodeOpeChutage")!.Value;
            SetChamp(d, "message", "CodeOpeDecoupe", codeOpeChutage);
        });

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
