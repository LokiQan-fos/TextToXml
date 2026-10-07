using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

    // The tables the dispatch inserts into (Kape22Persister) or deletes from (DeleteOf).
    private static readonly string[] DispatchTables =
    [
        "L_D_CONSIGNES", "L_D_COULEE", "L_D_KAPE22", "L_D_MAM_QUAL", "L_D_OF_SUIVI", "L_D_ORDRE_FABRICATION",
        "L_D_PRODUITS_OUTIL", "L_D_REBUT", "L_D_SECTIONCHARGE_CHUTAGE", "L_D_SECTIONCHARGE_DECOUPE",
        "L_D_SECTIONCHARGE_LINGOT", "L_D_SECTIONCHARGE_PITS", "L_D_SECTIONCHARGE_POIDSMETRIQUE",
        "L_D_SECTIONCHARGE_REFROIDISSOIRS", "L_D_SECTIONCHARGE_SVT",
    ];

    // The production foreign keys touching the dispatch, exactly as read in AFV004-LSI sys.foreign_keys
    // on 2026-10-06 (all NO_ACTION, trusted, enabled), as (Name, Table, Column, ReferencedTable,
    // ReferencedColumn). The keys from other tables to L_D_COULEE (L_D_ANOMALIES, L_D_PLANS_FOURS, L_D_PSO,
    // L_D_REBUT.molding_id, L_D_STOCK_PSO) are out of scope: the dispatch only inserts L_D_COULEE rows.
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

            AssertAccepted(Persist(fixture, created));
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

            ImportResult replaced = Persist(fixture, Hot(numeroFichier: "999"));

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

        // Spec acceptance criterion 2: no key outlives the test.
        Assert.Equal(0, ForeignKeyCount(fixture.AscoLsiConnectionString));
    }

    // Drift guard for the hard-coded list: every production key with a dispatch table on either side is one
    // of the 20, NO_ACTION, enabled and trusted - except the keys from another table to L_D_COULEE, which the
    // dispatch only ever inserts, so none of its writes can break them. Needs the production database, read
    // only (CC-7); the guard above runs without it.
    [SkippableFact]
    [Trait("AC", "FR21-7")]
    public void ProductionForeignKeys_TouchingTheDispatch_AreExactlyTheGuardedList_AcFr21_7()
    {
        Skip.If(string.IsNullOrWhiteSpace(ProductionConnectionString), NoProductionSkipReason);

        string tables = string.Join(", ", DispatchTables.Select(table => $"N'{table}'"));
        List<string> actual = [];
        using SqlConnection production = OpenProduction();
        using SqlCommand command = new(
            "SELECT fk.name, OBJECT_NAME(fk.parent_object_id), pc.name, OBJECT_NAME(fk.referenced_object_id), rc.name, " +
            "fk.delete_referential_action_desc, fk.is_disabled, fk.is_not_trusted " +
            "FROM sys.foreign_keys fk " +
            "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " +
            "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " +
            "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " +
            $"WHERE (OBJECT_NAME(fk.parent_object_id) IN ({tables}) OR OBJECT_NAME(fk.referenced_object_id) IN ({tables})) " +
            "AND NOT (OBJECT_NAME(fk.referenced_object_id) = N'L_D_COULEE' AND OBJECT_NAME(fk.parent_object_id) <> N'L_D_ORDRE_FABRICATION');",
            production);
        using (SqlDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                actual.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}|{reader.GetString(4)}|" +
                    $"{reader.GetString(5)}|{(reader.GetBoolean(6) ? 1 : 0)}{(reader.GetBoolean(7) ? 1 : 0)}");
            }
        }

        Assert.Equal(
            ForeignKeys
                .Select(key => $"{key.Name}|{key.Table}|{key.Column}|{key.ReferencedTable}|{key.ReferencedColumn}|NO_ACTION|00")
                .Order(StringComparer.OrdinalIgnoreCase),
            actual.Order(StringComparer.OrdinalIgnoreCase));
    }

    // The reference Fichier with a hot CodeConsignePits, so its absent Coulee "065718" is created, not
    // refused, and its PoidsMetrique / SVT sections made applicable (codes from the 2026-10-06 list), so
    // all 7 sections - and their 14 keys - are written by CREATE and deleted by REPLACE (GPAO no longer
    // sends those 2 legacy sections, but the mapper still writes them if it does); numeroFichier
    // re-sends it under another NumeroFichier, past the D22 guard.
    private static Kape22ImportBundle Hot(string? numeroFichier = null) =>
        MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "CodeConsignePits", "3 148 00 740");
            SetChamp(d, "message", "CodeOpePoidMetrique", "XP9");
            SetChamp(d, "message", "CodeOpeSVT", "XVP");
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "RangOpePoidMetrique", "160");
            SetChamp(d, "message", "RangOpeSVT", "170");
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

        // A null section would leave its 2 keys unexercised while the test stays green.
        Assert.All(codeOperations, code => Assert.NotNull(code));

        string profil = bundle.OrdreFabrication!.ProfilProduit;
        Assert.True(
            profils.Contains(profil),
            $"Spec Ask First (reference data, not an FK order bug): ProfilProduit '{profil}' is missing from L_P_PROFIL_PRODUIT.");
        foreach (string code in codeOperations.Cast<string>())
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
}
