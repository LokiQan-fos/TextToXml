using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 6.11 (AC-FR21-7): the schema mirror carries no foreign key (it would block the fixture's TRUNCATE
// reset), so nothing else guards the order in which the dispatch inserts (EF Core's command sort, AD-7,
// no declared relation) and deletes (Kape22Persister.DeleteOf, children first, OF last) against the
// production foreign keys. This test adds them to AscoLSI_Test for its own duration only, after filling
// the 2 referenced L_P_* tables - copied from production (SELECT only, CC-7) when
// ConnectionStrings:AscoLSI_Production is configured, otherwise from the codes read there on 2026-10-06 -
// then creates an OF on a new (hot) Coulee and replaces it (D34) with L_D_OF_SUIVI / L_D_REBUT rows
// present. The keys are dropped in a finally, even on failure. Not opt-in: it skips only when the test
// instance is unavailable, so the guard also runs in CI. Integration category (AR-12), commit + reset.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class ProductionForeignKeyOrderIntegrationTests(SqlServerIntegrationFixture fixture)
{
    // The CodeOperation keys of production L_P_TEXT_OPERATIONS, read in AFV004-LSI on 2026-10-06; the
    // fallback when no production database is configured.
    private static readonly string[] CodeOperations =
    [
        "DS2", "FC1", "FD1", "FD2", "FD3", "LA1", "LA9", "NLT", "PC1", "VD1",
        "VD2", "VD9", "XA1", "XA2", "XC1", "XP1", "XP9", "XV5", "XVP",
    ];

    // The production foreign keys touching the dispatch, exactly as read in AFV004-LSI sys.foreign_keys
    // on 2026-10-06 (all NO_ACTION, trusted, enabled), as (Name, Table, Column, ReferencedTable,
    // ReferencedColumn). The L_D_PLANS_FOURS.IdCoulee, L_D_PSO.Coulee and L_D_REBUT.molding_id keys to
    // L_D_COULEE are out of scope: those columns are not mirrored and the dispatch never writes them.
    private static readonly (string Name, string Table, string Column, string ReferencedTable, string ReferencedColumn)[] ForeignKeys =
    [
        ("FK_ConsignesChutageOrdreFabrication", "L_D_SECTIONCHARGE_CHUTAGE", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesDecoupeLingotOrdreFabrication", "L_D_SECTIONCHARGE_DECOUPE", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesEnfournementPitsOrdreFabrication", "L_D_SECTIONCHARGE_PITS", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesLingotOrdreFabrication", "L_D_SECTIONCHARGE_LINGOT", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesPoidsMetriqueOrdreFabrication", "L_D_SECTIONCHARGE_POIDSMETRIQUE", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesRefroidissoirOrdreFabrication", "L_D_SECTIONCHARGE_REFROIDISSOIRS", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_ConsignesSVTOrdreFabrication", "L_D_SECTIONCHARGE_SVT", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_CouleeOrdreFabrication", "L_D_ORDRE_FABRICATION", "Coulee", "L_D_COULEE", "IdCoulee"),
        ("fk_l_d_rebut_of", "L_D_REBUT", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_OrdreFabricationProfilProduit", "L_D_ORDRE_FABRICATION", "ProfilProduit", "L_P_PROFIL_PRODUIT", "ID"),
        ("FK_OrdreFabricationPSO", "L_D_PSO", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_PlanFourOrdreFabrication", "L_D_PLANS_FOURS", "OF", "L_D_ORDRE_FABRICATION", "OF"),
        ("FK_SectionChargeChutageSuiviConsigneLaminage", "L_D_SECTIONCHARGE_CHUTAGE", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SectionChargeDecoupeSuiviConsigneLaminage", "L_D_SECTIONCHARGE_DECOUPE", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SectionChargeEnfournementPitsSuiviConsigneLaminage", "L_D_SECTIONCHARGE_PITS", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SectionChargeLingoSuiviConsigneLaminage", "L_D_SECTIONCHARGE_LINGOT", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SectionChargePoidsMetriqueSuiviConsigneLaminage", "L_D_SECTIONCHARGE_POIDSMETRIQUE", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SectionChargeRefroidissoirSuiviConsigneLaminage", "L_D_SECTIONCHARGE_REFROIDISSOIRS", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SuiviConsigneLaminageConsignesSVT", "L_D_SECTIONCHARGE_SVT", "CodeOperation", "L_P_TEXT_OPERATIONS", "CodeOperation"),
        ("FK_SuiviDeOFOrdreFabrication", "L_D_OF_SUIVI", "OF", "L_D_ORDRE_FABRICATION", "OF"),
    ];

    // The ID keys of production L_P_PROFIL_PRODUIT, read in AFV004-LSI on 2026-10-06; the fallback when
    // no production database is configured.
    private static readonly string[] ProfilProduits = ["BIL", "BLO", "LAR", "RON"];

    // The 2 production reference tables the keys point to; key-only mirrors in scripts/schema/.
    private static readonly string[] ReferencedTables = ["L_P_PROFIL_PRODUIT", "L_P_TEXT_OPERATIONS"];

    [SkippableFact]
    [Trait("AC", "FR21-7")]
    public void Persist_CreateThenReplaceUnderProductionForeignKeys_ViolatesNone_AcFr21_7()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");

        fixture.ResetData();
        try
        {
            FillReferencedTables();

            // CREATE: the reference Fichier made hot, its Coulee absent, so L_D_COULEE is inserted too.
            Kape22ImportBundle created = Hot();
            AssertReferenceCodesPresent(created);

            // Added before CREATE, so the INSERT order is guarded as well as the delete order.
            Execute(string.Concat(ForeignKeys.Select(key =>
                $"ALTER TABLE dbo.{key.Table} WITH CHECK ADD CONSTRAINT [{key.Name}] " +
                $"FOREIGN KEY ([{key.Column}]) REFERENCES dbo.{key.ReferencedTable} ([{key.ReferencedColumn}]);")));

            AssertAccepted(Persist(created));
            string of = DownstreamOf.Pad(created.OF!);

            // REPLACE (D34, Etat GPAO): the OF now has an L_D_OF_SUIVI row and 2 L_D_REBUT rows, which
            // reference it and must go before it.
            using (AscoLsiDbContext context = fixture.NewAscoLsiContext())
            {
                context.Database.ExecuteSqlRaw(
                    "INSERT INTO dbo.L_D_OF_SUIVI ([OF], [Rang]) VALUES ({0}, 1); " +
                    "INSERT INTO dbo.L_D_REBUT ([OF]) VALUES ({0}), ({0});",
                    of);
            }

            ImportResult replaced = Persist(Hot(numeroFichier: "999"));

            AssertAccepted(replaced);
            Assert.False(replaced.AlreadyImported);
            using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
            L_D_ORDRE_FABRICATION row = Assert.Single(verify.OrdreFabricationRows.AsNoTracking());
            Assert.Equal(of, row.OF);
            Assert.Equal("999", row.NumeroFichier.Trim());
            Assert.Empty(verify.OfSuiviRows.AsNoTracking());
            Assert.Empty(verify.RebutRows.AsNoTracking());
        }
        finally
        {
            // CLEANUP: the keys go first, or the next ResetData's TRUNCATE fails; then the 2 L_P_* tables,
            // which ResetData does not empty.
            Execute(string.Concat(ForeignKeys.Select(key =>
                $"IF OBJECT_ID(N'dbo.{key.Name}', N'F') IS NOT NULL ALTER TABLE dbo.{key.Table} DROP CONSTRAINT [{key.Name}];")));
            Execute(string.Concat(ReferencedTables.Select(table => $"DELETE FROM dbo.{table};")));
        }
    }

    private static void AssertAccepted(ImportResult result) =>
        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));

    // The reference Fichier with a hot CodeConsignePits, so its absent Coulee "065718" is created, not
    // refused; numeroFichier re-sends it under another NumeroFichier, past the D22 guard.
    private static Kape22ImportBundle Hot(string? numeroFichier = null) =>
        MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "CodeConsignePits", "3 148 00 740");
            SetChamp(d, "message", "Coulee", "065718");
            if (numeroFichier is not null)
            {
                SetChamp(d, "header", "NumeroFichier", numeroFichier);
            }
        });

    // Spec Ask First guard: a ProfilProduit or CodeOperation the reference tables lack would fail CREATE on
    // an FK and read as an insert-order bug; it is a reference-data case, so it fails here, named as such.
    private void AssertReferenceCodesPresent(Kape22ImportBundle bundle)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        List<string> operations = [.. context.Database.SqlQueryRaw<string>("SELECT [CodeOperation] AS [Value] FROM dbo.L_P_TEXT_OPERATIONS")];
        List<string> profils = [.. context.Database.SqlQueryRaw<string>("SELECT [ID] AS [Value] FROM dbo.L_P_PROFIL_PRODUIT")];
        string?[] codeOperations =
        [
            bundle.SectionChargeChutage?.CodeOperation,
            bundle.SectionChargeDecoupe?.CodeOperation,
            bundle.SectionChargeLingot?.CodeOperation,
            bundle.SectionChargePits?.CodeOperation,
            bundle.SectionChargePoidsMetrique?.CodeOperation,
            bundle.SectionChargeRefroidissoirs?.CodeOperation,
            bundle.SectionChargeSvt?.CodeOperation,
        ];

        string profil = bundle.OrdreFabrication!.ProfilProduit;
        Assert.True(
            profils.Contains(profil),
            $"Spec Ask First (reference data, not an FK order bug): ProfilProduit '{profil}' is missing from L_P_PROFIL_PRODUIT.");
        foreach (string code in codeOperations.OfType<string>())
        {
            Assert.True(
                operations.Contains(code),
                $"Spec Ask First (reference data, not an FK order bug): CodeOperation '{code}' is missing from L_P_TEXT_OPERATIONS.");
        }
    }

    private void Execute(string commandText)
    {
        using SqlConnection connection = new(fixture.AscoLsiConnectionString);
        connection.Open();
        using SqlCommand command = new(commandText, connection);
        command.ExecuteNonQuery();
    }

    // Empties the 2 L_P_* tables first (an interrupted finally may have left rows), then copies them from
    // production when configured, otherwise inserts the 2026-10-06 codes.
    private void FillReferencedTables()
    {
        Execute(string.Concat(ReferencedTables.Select(table => $"DELETE FROM dbo.{table};")));
        if (!string.IsNullOrWhiteSpace(ProductionConnectionString))
        {
            foreach (string table in ReferencedTables)
            {
                CopyFromProduction(fixture.AscoLsiConnectionString, table, "1 = 1");
            }

            return;
        }

        Execute(
            string.Concat(ProfilProduits.Select(code => $"INSERT INTO dbo.L_P_PROFIL_PRODUIT ([ID]) VALUES (N'{code}');")) +
            string.Concat(CodeOperations.Select(code => $"INSERT INTO dbo.L_P_TEXT_OPERATIONS ([CodeOperation]) VALUES (N'{code}');")));
    }

    private ImportResult Persist(Kape22ImportBundle bundle)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Import:Commande"] = "P60" })
            .Build();
        return new Kape22Persister(context, configuration, fixture.NewJournal(), ReferenceFichierName, WinterClock()).Persist(bundle);
    }
}
