using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// AR-12 harness. A single fixture for the whole integration collection: it reads the test connection
// strings from configuration, briefly probes the instance, and applies the idempotent scripts/schema/
// files. When no instance is reachable it records a skip reason instead of failing, so the suite stays
// green on a machine without SQL Server. Per-test isolation is the caller's job (TransactionScope +
// rollback by default; commit + reset where a test depends on committed state).
public sealed class SqlServerIntegrationFixture
{
    // Split a script into batches on lines containing only GO, the way sqlcmd does.
    private static readonly Regex BatchSeparator = new(
        @"^\s*GO\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public SqlServerIntegrationFixture()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables("KAPE22_TEST_")
            .Build();

        AscoLsiConnectionString = configuration.GetConnectionString("AscoLSI") ?? string.Empty;
        MqttConnectionString = configuration.GetConnectionString("MQTTnetServices") ?? string.Empty;

        // Story 2.1 AC: the harness creates its tables (L_D_KAPE22, L_D_LOG_COMMANDE, MQTTnetServices
        // dbo.Logs), and AscoLSI + MQTTnetServices live on the same instance, so both connection
        // strings are required. A partial configuration skips rather than reporting a half-applied
        // schema as ready.
        if (string.IsNullOrWhiteSpace(AscoLsiConnectionString) || string.IsNullOrWhiteSpace(MqttConnectionString))
        {
            SkipReason =
                "No SQL Server test instance configured. Set ConnectionStrings:AscoLSI and " +
                "ConnectionStrings:MQTTnetServices in tests/Kape22Importer.Tests/appsettings.Test.json " +
                "(see appsettings.Test.json.example) or the KAPE22_TEST_ConnectionStrings__* " +
                "environment variables.";
            return;
        }

        try
        {
            ApplySchema(AscoLsiConnectionString, "01-ascolsi-tables.sql");
            ApplySchema(MqttConnectionString, "02-mqtt-tables.sql");

            Available = true;
        }
        catch (Exception exception)
            when (exception is SqlException or InvalidOperationException or TimeoutException or ArgumentException)
        {
            SkipReason = $"SQL Server test instance not reachable: {exception.Message}";
        }
    }

    public string AscoLsiConnectionString { get; }

    // True when the instance answered and the schema is in place; false means the integration tests skip.
    public bool Available { get; }

    public string MqttConnectionString { get; }

    // Set when Available is false; carries the actionable reason to show in the skipped test.
    public string? SkipReason { get; }

    // Commit + reset isolation regime (AR-12): empties the two AscoLSI harness tables and reseeds their
    // identity, for the Story 2.8 tests that depend on committed state between two actions (the D22
    // anti-duplicate guard, AC-FR11-6/11-7) or that verify a transaction boundary (AC-FR11-3/11-5) and
    // therefore cannot run under an ambient TransactionScope. Each such test calls this first.
    public void ResetData()
    {
        ExecuteNonQuery(
            AscoLsiConnectionString,
            "TRUNCATE TABLE dbo.L_D_KAPE22; TRUNCATE TABLE dbo.L_D_LOG_COMMANDE;");
    }

    // Empties the MQTTnetServices.Logs harness table, for the Story 3.3 tests that assert on the rows
    // the real Serilog.Sinks.MSSqlServer sink writes (FR-14). DELETE, not TRUNCATE: Logs has an identity
    // column the assertions never read, and a shared instance may deny TRUNCATE.
    public void ResetMqttLogs()
    {
        ExecuteNonQuery(MqttConnectionString, "DELETE FROM dbo.Logs;");
    }

    // A fresh context bound to the test instance. The caller owns its lifetime and its transaction.
    public AscoLsiDbContext NewAscoLsiContext()
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer(WithShortLoginTimeout(AscoLsiConnectionString))
            .Options;

        return new AscoLsiDbContext(options);
    }

    // A short login timeout keeps an instance that dies mid-run a quick failure rather than a long hang.
    private static string WithShortLoginTimeout(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 }.ConnectionString;

    private static void ExecuteNonQuery(string connectionString, string commandText)
    {
        using SqlConnection connection = new(WithShortLoginTimeout(connectionString));
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void ApplySchema(string connectionString, string scriptFileName)
    {
        EnsureDatabaseExists(connectionString);
        DropExistingUserTables(connectionString);

        string path = RepoLayout.ProjectFile(Path.Combine("scripts", "schema", scriptFileName));
        string script = File.ReadAllText(path);

        using SqlConnection connection = new(WithShortLoginTimeout(connectionString));
        connection.Open();

        foreach (string batch in BatchSeparator.Split(script))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            using SqlCommand command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandType = CommandType.Text;
            command.ExecuteNonQuery();
        }
    }

    // A CI SQL Server service container starts with only 'master'; creating the test database here
    // (instead of a separate CI provisioning step) keeps CI and a fresh local instance on one path.
    private static void EnsureDatabaseExists(string connectionString)
    {
        SqlConnectionStringBuilder builder = new(WithShortLoginTimeout(connectionString));
        string database = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        using SqlConnection connection = new(builder.ConnectionString);
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            IF DB_ID(@name) IS NULL
            BEGIN
                DECLARE @sql nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@name);
                EXEC sp_executesql @sql;
            END
            """;
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar) { Value = database });
        command.ExecuteNonQuery();
    }

    // F-2 (Epic 3 retro): a table left over from an earlier schema (dbo.WorkerSettings, pre-Story 3.0)
    // never gets removed by the idempotent CREATE scripts, so a test instance that outlives a schema
    // change reports the wrong table count. Dropping every non-system table before each ApplySchema
    // guarantees the instance always matches exactly what scripts/schema/ currently describes.
    private static void DropExistingUserTables(string connectionString)
    {
        using SqlConnection connection = new(WithShortLoginTimeout(connectionString));
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE t.is_ms_shipped = 0;
            EXEC sp_executesql @sql;
            """;
        command.ExecuteNonQuery();
    }
}
