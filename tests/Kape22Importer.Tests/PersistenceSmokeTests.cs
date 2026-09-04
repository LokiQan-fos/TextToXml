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
// regime). Skips with a clear message when no instance is configured.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
[Trait("AC", "2.1")]
public class PersistenceSmokeTests(SqlServerIntegrationFixture fixture)
{
    [SkippableFact]
    public void SchemaApplies_CreatesExactlyTheFourHarnessTables()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");

        // Story 2.1 AC: the harness creates only these four tables and nothing else from the real
        // database.
        Assert.True(TableExists(fixture.AscoLsiConnectionString, "L_D_KAPE22"));
        Assert.True(TableExists(fixture.AscoLsiConnectionString, "L_D_LOG_COMMANDE"));
        Assert.True(TableExists(fixture.MqttConnectionString, "Logs"));
        Assert.True(TableExists(fixture.MqttConnectionString, "WorkerSettings"));

        Assert.Equal(2, UserTableCount(fixture.AscoLsiConnectionString));
        Assert.Equal(2, UserTableCount(fixture.MqttConnectionString));
    }

    [SkippableFact]
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
