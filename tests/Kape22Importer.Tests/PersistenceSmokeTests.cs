using System;
using System.Data;
using System.Linq;
using System.Transactions;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.1 smoke test for the AR-12 harness: the fixture connects, scripts/schema/ applies without
// error, and an INSERT/SELECT round-trips on L_D_KAPE22. The write happens inside a TransactionScope
// that is never completed, so the row rolls back and the test instance stays clean (default isolation
// regime). Skips with a clear message when no instance is configured. AC trait lives per method (not
// on the class) since Story 4.1 extends this same harness with its own 10 tables.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class PersistenceSmokeTests(SqlServerIntegrationFixture fixture)
{
    // The 10 downstream dispatch tables added in Story 4.1 (AFV004-LSI sys.tables, 2026-09-14).
    private static readonly string[] DownstreamTables =
    [
        "L_D_ORDRE_FABRICATION",
        "L_D_COULEE",
        "L_D_CONSIGNES",
        "L_D_SECTIONCHARGE_CHUTAGE",
        "L_D_SECTIONCHARGE_DECOUPE",
        "L_D_SECTIONCHARGE_LINGOT",
        "L_D_SECTIONCHARGE_PITS",
        "L_D_SECTIONCHARGE_POIDSMETRIQUE",
        "L_D_SECTIONCHARGE_REFROIDISSOIRS",
        "L_D_SECTIONCHARGE_SVT",
    ];

    [SkippableFact]
    [Trait("AC", "2.1")]
    [Trait("AC", "4.1")]
    public void SchemaApplies_CreatesExactlyTheHarnessTables()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");

        // Story 2.1 AC: the harness creates only these tables and nothing else from the real
        // database. dbo.WorkerSettings is Launcher-owned (2026-09-09 correction of course) and is
        // no longer part of the importer harness.
        Assert.True(TableExists(fixture.AscoLsiConnectionString, "L_D_KAPE22"));
        Assert.True(TableExists(fixture.AscoLsiConnectionString, "L_D_LOG_COMMANDE"));
        Assert.True(TableExists(fixture.MqttConnectionString, "Logs"));

        // Story 4.1: the 10 downstream dispatch tables (AFV004-LSI, sys.columns/sys.indexes, 2026-09-14).
        foreach (string table in DownstreamTables)
        {
            Assert.True(TableExists(fixture.AscoLsiConnectionString, table), $"Missing table dbo.{table}.");
        }

        Assert.Equal(2 + DownstreamTables.Length, UserTableCount(fixture.AscoLsiConnectionString));
        Assert.Equal(1, UserTableCount(fixture.MqttConnectionString));
    }

    // Story 4.1 AC: scripts/schema/ applies without error for the 10 downstream tables, exercised here
    // with one minimal insert per table (only the real NOT NULL columns, AFV004-LSI sys.columns
    // 2026-09-14) inside a single TransactionScope that is never completed, so nothing survives.
    [SkippableFact]
    [Trait("AC", "4.1")]
    public void SchemaApplies_AndAllTenDownstreamTablesRoundTripUnderRollback()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");

        using (TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled))
        {
            using AscoLsiDbContext context = fixture.NewAscoLsiContext();
            context.Database.OpenConnection();

            context.OrdreFabricationRows.Add(new L_D_ORDRE_FABRICATION
            {
                AcompteSolde = "S",
                ClasseDeChute = "CHUT",
                CodeDemiProduit = "DEMI",
                DateMaj = new DateTime(2026, 9, 14, 0, 0, 0),
                DateReception = new DateTime(2026, 9, 14, 0, 0, 0),
                DiametreProduit = 1.0m,
                Epaisseur = 1.0m,
                Etat = 1,
                Indice = 0,
                LongueurCD = 1.000m,
                MarqueCommerciale = "MARQUE",
                Nuance = "AISI304",
                Client = "APERAM",
                NombreDemiProduit = 1,
                NumeroFichier = "108",
                NumeroMontage = "MNT",
                OF = "000000123456",
                PoidsPesee = 1,
                ProfilProduit = "PRD",
                ToleranceMaxEpaisseur = 1.0m,
                ToleranceMaxLongueur = 1m,
                ToleranceMaxSection = 1.0m,
                ToleranceMinEpaisseur = 1.0m,
                ToleranceMinLongueur = 1m,
                ToleranceMinSection = 1.0m,
                Type = "C",
            });

            context.CouleeRows.Add(new L_D_COULEE
            {
                DateReception = new DateTime(2026, 9, 14, 0, 0, 0),
                DerniereModif = new DateTime(2026, 9, 14, 0, 0, 0),
                EtatReception = 1,
                Externe = false,
                IdCoulee = "063127",
                NbLingotRestantARefroidir = 0,
                Nuance = "AISI304",
            });

            context.ConsignesRows.Add(new L_D_CONSIGNES
            {
                CodeConsigne = "CODE",
                CodeOperation = "COD",
                ConsigneGPAO = false,
                OF = "000000123456",
                SizeCodeConsigne = 4,
                TypeConsigne = 1,
            });

            context.SectionChargeChutageRows.Add(new L_D_SECTIONCHARGE_CHUTAGE
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargeDecoupeRows.Add(new L_D_SECTIONCHARGE_DECOUPE
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargeLingotRows.Add(new L_D_SECTIONCHARGE_LINGOT
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargePitsRows.Add(new L_D_SECTIONCHARGE_PITS
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargePoidsMetriqueRows.Add(new L_D_SECTIONCHARGE_POIDSMETRIQUE
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargeRefroidissoirsRows.Add(new L_D_SECTIONCHARGE_REFROIDISSOIRS
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SectionChargeSvtRows.Add(new L_D_SECTIONCHARGE_SVT
            {
                CodeOperation = "COD",
                OF = "000000123456",
                RangOperation = "R01",
            });

            context.SaveChanges();

            // No scope.Complete(): every insert above rolls back on dispose.
        }
    }

    [SkippableFact]
    [Trait("AC", "2.1")]
    public void SchemaApplies_AndKape22RowRoundTripsUnderRollback()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");

        int newId;

        using (TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled))
        {
            using AscoLsiDbContext context = fixture.NewAscoLsiContext();

            // Pin one physical connection for the whole scope so the ambient transaction stays local
            // (no MSDTC escalation) across the insert and the read-back.
            context.Database.OpenConnection();

            L_D_KAPE22 row = new()
            {
                Client = "APERAM",
                Coulee = "063127",
                DateReception = new DateTime(2026, 9, 4, 10, 0, 0),
                Indice = 0,
                NumeroFichier = "108",
                Nuance = "AISI304",
                OF = "000000123456",
                Type = "C",
            };

            context.Kape22Rows.Add(row);
            context.SaveChanges();
            newId = row.Id;

            Assert.True(newId > 0, "SQL Server should assign the identity value on insert.");

            L_D_KAPE22 reloaded = context.Kape22Rows.AsNoTracking().Single(entity => entity.Id == newId);
            Assert.Equal("063127", reloaded.Coulee);
            Assert.Equal(new DateTime(2026, 9, 4, 10, 0, 0), reloaded.DateReception);

            // No scope.Complete(): the ambient transaction rolls back on dispose.
        }

        using AscoLsiDbContext afterRollback = fixture.NewAscoLsiContext();
        Assert.False(
            afterRollback.Kape22Rows.Any(entity => entity.Id == newId),
            "The row must not survive the rolled-back TransactionScope.");
    }

    private static bool TableExists(string connectionString, string table)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(@name, N'U');";
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar) { Value = $"dbo.{table}" });

        return command.ExecuteScalar() is not (null or DBNull);
    }

    private static int UserTableCount(string connectionString)
    {
        using SqlConnection connection = new(connectionString);
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;";

        return (int)command.ExecuteScalar()!;
    }
}
