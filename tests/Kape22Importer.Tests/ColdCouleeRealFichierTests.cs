using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AscoLsiJournal;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 6.9 (AC-FR20-5): real-Fichier regression for the cold-Coulee check. P60/error/ holds Fichiers the
// legacy chain rejected because their cold Coulee was not yet in L_D_COULEE; each one goes through the
// real Kape22FichierProcessor against AscoLSI_Test, with the production state it met rebuilt in the test
// from the Fichiers themselves (no production connection, CC-7). Before Story 6.9 the persister compared
// the whole 12-character CodeConsignePits with "1", so 407 was accepted and 408/430 hit a raw
// PK_L_D_CONSIGNES PersistenceError instead of the business cause. Integration category (AR-12): skips
// cleanly without the local SQL Server test instance; commit + reset regime (ResetData first).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class ColdCouleeRealFichierTests(SqlServerIntegrationFixture fixture)
{
    // REAL_COLD_EXISTING_OF: 428 created OF 2040297 on cold Coulee 063196, which was then missing when
    // 430 re-sent the same OF.
    [SkippableFact]
    [Trait("AC", "FR20-5")]
    public void Import_ColdCouleeMissingOnAnExistingOf_IsRejectedForTheMissingCoulee_AcFr20_5()
    {
        Ready();
        Assert.Equal("063196", SeedCouleeOf("P60_847_682_428"));
        ImportResult setup = Import("P60_847_682_428");
        Assert.True(setup.Success, string.Join("; ", setup.Errors.Select(error => error.Message)));
        Assert.Equal(1, DeleteCoulee("063196"));
        int[] before = Counts();

        ImportResult result = Import("error/P60_847_682_430");

        AssertRejectedForMissingCoulee(result, "2040297", "063196", before, logRowsBefore: 1);
        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Equal(setup.InsertedId, Assert.Single(verify.Kape22Rows.AsNoTracking()).Id);
    }

    // REAL_COLD_MISSING then REAL_COLD_MISSING_AFTER: 407 and 408 name cold Coulee 063241, not yet in
    // L_D_COULEE - both are refused for that cause, never a PersistenceError.
    [SkippableFact]
    [Trait("AC", "FR20-5")]
    public void Import_ColdCouleeMissing_RejectsBothFichiersForTheMissingCoulee_AcFr20_5()
    {
        Ready();

        int[] empty = Counts();
        Assert.All(empty, count => Assert.Equal(0, count));

        AssertRejectedForMissingCoulee(Import("error/P60_847_682_407"), "2040278", "063241", empty, logRowsBefore: 0);
        AssertRejectedForMissingCoulee(Import("error/P60_847_682_408"), "2040278", "063241", empty, logRowsBefore: 1);
    }

    // REAL_COLD_PRESENT: once cold Coulee 063241 is on file, 412 (accepted by the legacy chain) imports.
    [SkippableFact]
    [Trait("AC", "FR20-5")]
    public void Import_ColdCouleePresent_IsAccepted_AcFr20_5()
    {
        Ready();
        SeedCouleeOf("P60_847_682_412");

        ImportResult result = Import("P60_847_682_412");

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));
        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
    }

    private static byte[] Read(string relativePath) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"P60/{relativePath}"));

    // A fixed instant after every Fichier of this class was received (AR-12).
    private static TimeProvider Clock() =>
        new FixedClock(DateTimeOffset.Parse("2026-10-01T12:00:00Z", CultureInfo.InvariantCulture));

    private void AssertRejectedForMissingCoulee(ImportResult result, string of, string coulee, int[] before, int logRowsBefore)
    {
        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Equal($"OF '{of}' : la coulée '{coulee}' est introuvable dans L_D_COULEE.", error.Message);

        // No business row in L_D_KAPE22 or any of the 10 downstream tables, one new REJETÉ journal entry.
        Assert.Equal(before, Counts());
        List<L_D_LOG_COMMANDE> logRows = fixture.LogRows();
        Assert.Equal(logRowsBefore + 1, logRows.Count);
        Assert.Contains("REJETÉ", logRows[^1].Message);
        Assert.Contains(coulee, logRows[^1].Message, StringComparison.Ordinal);
    }

    // Row counts of L_D_KAPE22 and the 10 downstream tables (L_D_COULEE, L_D_ORDRE_FABRICATION, the 7
    // L_D_SECTIONCHARGE_* and L_D_CONSIGNES): 11 entries.
    private int[] Counts()
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        return
        [
            context.ConsignesRows.Count(),
            context.CouleeRows.Count(),
            context.Kape22Rows.Count(),
            context.OrdreFabricationRows.Count(),
            context.SectionChargeChutageRows.Count(),
            context.SectionChargeDecoupeRows.Count(),
            context.SectionChargeLingotRows.Count(),
            context.SectionChargePitsRows.Count(),
            context.SectionChargePoidsMetriqueRows.Count(),
            context.SectionChargeRefroidissoirsRows.Count(),
            context.SectionChargeSvtRows.Count(),
        ];
    }

    // Removes the Coulee from L_D_COULEE and returns how many rows the delete removed.
    private int DeleteCoulee(string coulee)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.CouleeRows.RemoveRange(context.CouleeRows.Where(row => row.IdCoulee == coulee));
        return context.SaveChanges();
    }

    private ImportResult Import(string relativePath) =>
        new Kape22FichierProcessor(
                fixture.NewAscoLsiContext,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Import:Commande"] = "P60",
                    ["Import:InitiatingServer"] = SqlServerIntegrationFixture.JournalInitiatingServer,
                }).Build(),
                fixture.NewJournal(),
                new ImportOptions
                {
                    ArchiveFolder = "archive",
                    ErrorFolder = "error",
                    InboxPath = "inbox",
                    InitiatingServer = SqlServerIntegrationFixture.JournalInitiatingServer,
                    PollingInterval = TimeSpan.FromSeconds(30),
                    ProcessingFolder = "processing",
                    RetentionDays = 30,
                },
                Clock(),
                NullLogger<Kape22FichierProcessor>.Instance)
            .Import(Path.GetFileName(relativePath), Read(relativePath));

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
    }

    // Puts the Fichier's own Coulee in L_D_COULEE, the state a cold Coulee is in once AscoLSI tracks it,
    // and returns its IdCoulee.
    private string SeedCouleeOf(string relativePath)
    {
        Kape22ImportBundle bundle = TestSupport.MapFichier(Read(relativePath), Path.GetFileName(relativePath));
        TestSupport.SeedCoulee(fixture.NewAscoLsiContext, bundle);
        return bundle.Coulee!.IdCoulee.Trim();
    }
}
