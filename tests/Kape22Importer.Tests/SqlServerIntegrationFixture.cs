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

        // Story 2.1 AC: the harness creates all four tables, and AscoLSI + MQTTnetServices live on the
        // same instance, so both connection strings are required. A partial configuration skips rather
        // than reporting a half-applied schema as ready.
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

    private static void ApplySchema(string connectionString, string scriptFileName)
    {
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
}
