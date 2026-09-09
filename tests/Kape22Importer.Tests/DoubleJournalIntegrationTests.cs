using System;
using System.Collections.Generic;
using System.Data;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Sinks.MSSqlServer;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.3 (FR-14): the one real round-trip through Serilog.Sinks.MSSqlServer, the sink every portal
// worker uses to write MQTTnetServices.dbo.Logs. Kape22FichierProcessor logs through ILogger; here a
// Serilog logger backed by the MSSqlServer sink is wired behind that ILogger, a Fichier is processed,
// the sink is flushed, and dbo.Logs is read back to prove the line lands with the right Level and the
// "[Kape22Importer][<Event>] : ..." message. The L_D_LOG_COMMANDE half stays on Kape22Persister and is
// asserted alongside. Integration category (AR-12): needs a reachable local SQL Server test instance
// (AscoLSI_Test + MQTTnetServices_Test) and skips cleanly otherwise. Commit + reset regime: ResetData
// and ResetMqttLogs first, because the rows are read back after the transaction commits. Written
// test-first (CC-1). Vocabulary follows the PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class DoubleJournalIntegrationTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    private static ImportOptions Options() => new()
    {
        ArchiveFolder = "archive",
        ErrorFolder = "error",
        InboxPath = "inbox",
        InitiatingServer = InitiatingServer,
        PollingInterval = TimeSpan.FromSeconds(30),
        ProcessingFolder = "processing",
        RetentionDays = 30,
    };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
                ["Import:InitiatingServer"] = InitiatingServer,
            })
            .Build();

    // AC-FR14-1: a clean Fichier lands one Information row in MQTTnetServices.Logs, prefixed
    // "[Kape22Importer][ImportSucceeded]" and carrying the generated InsertedId, and commits the
    // L_D_KAPE22 + "— OK" L_D_LOG_COMMANDE rows in AscoLSI.
    [SkippableFact]
    [Trait("AC", "FR14-1")]
    public void Import_CleanFichier_WritesAnInformationRowToMqttLogs_AcFr14_1()
    {
        Ready();

        ImportResult result = RunWithSerilog(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Information", row.Level);
        Assert.Contains("[Kape22Importer][ImportSucceeded]", row.Message);
        Assert.Contains(result.InsertedId!.Value.ToString(), row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.EndsWith("— OK", Assert.Single(verify.LogCommandeRows.AsNoTracking()).Message);
    }

    // AC-FR14-2: a rejection with a readable OF lands one Error row in Logs listing the errors, and the
    // "<NumeroFichier> — REJETÉ" L_D_LOG_COMMANDE row is committed.
    [SkippableFact]
    [Trait("AC", "FR14-2")]
    public void Import_RejectionWithReadableOf_WritesAnErrorRowToMqttLogsAndTheRejeteLogCommande_AcFr14_2()
    {
        Ready();

        ImportResult result = RunWithSerilog(ReferenceFichierName, BlankClientReferenceFichier());

        Assert.False(result.Success);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Contains("REJETÉ", Assert.Single(verify.LogCommandeRows.AsNoTracking()).Message);
    }

    // AC-FR14-3 (D15): a structural rejection (Converter fails, OF never read) still lands one Error row
    // in Logs, and writes nothing at all to L_D_LOG_COMMANDE.
    [SkippableFact]
    [Trait("AC", "FR14-3")]
    public void Import_StructuralRejection_WritesOnlyTheMqttLogsErrorRow_AcFr14_3()
    {
        Ready();

        ImportResult result = RunWithSerilog(ReferenceFichierName, []);

        Assert.False(result.Success);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
    }

    // AC-FR14-8: an imported Fichier carrying coherence Warnings lands the Information success row plus a
    // second Warning row in Logs naming each warning code, both through the real Serilog level mapping
    // and MSSqlServer sink. The name "P60_999_682_001" diverges from the Header roulette
    // (FileNameMismatch) and Footer.Records "00009" is not 3 (InterBlockMismatch), while the bytes still
    // import.
    [SkippableFact]
    [Trait("AC", "FR14-8")]
    public void Import_SuccessWithCoherenceWarnings_WritesTheWarningRowToMqttLogs_AcFr14_8()
    {
        Ready();
        byte[] footerRecordsNotThree = WithText(ReadValidFixture(ReferenceFichierName), "00003", "00009");

        ImportResult result = RunWithSerilog("P60_999_682_001", footerRecordsNotThree);

        Assert.True(result.Success);

        List<LogRow> rows = ReadMqttLogs();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Level == "Information" && row.Message.Contains("[Kape22Importer][ImportSucceeded]"));
        LogRow warning = Assert.Single(rows, row => row.Level == "Warning");
        Assert.Contains("[Kape22Importer][CoherenceWarnings]", warning.Message);
        Assert.Contains(nameof(ErrorCode.FileNameMismatch), warning.Message);
        Assert.Contains(nameof(ErrorCode.InterBlockMismatch), warning.Message);
    }

    // AC-FR11-6 (D22): a Fichier whose key already carries a committed "— OK" L_D_LOG_COMMANDE row comes
    // back AlreadyImported with no new L_D_KAPE22 row, and lands one Warning row in Logs prefixed
    // "[Kape22Importer][AlreadyImported]" - through the real sink, not a RecordingLogger.
    [SkippableFact]
    [Trait("AC", "FR11-6")]
    public void Import_FichierAlreadyImported_WritesTheAlreadyImportedWarningRowToMqttLogs_AcFr11_6()
    {
        Ready();
        SeedOkLogRow();

        ImportResult result = RunWithSerilog(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.True(result.AlreadyImported);
        Assert.Null(result.InsertedId);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Warning", row.Level);
        Assert.Contains("[Kape22Importer][AlreadyImported]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Single(verify.LogCommandeRows.AsNoTracking());
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
        fixture.ResetMqttLogs();
    }

    // Seeds the committed "<NumeroFichier> — OK" L_D_LOG_COMMANDE row the D22 guard keys on, matching
    // what Kape22Persister.OkLogRowExists compares (the trimmed Detail OF, "<NumeroFichier> — OK").
    private void SeedOkLogRow()
    {
        MapResult<L_D_KAPE22> reference = MapReferenceFichier();
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.LogCommandeRows.Add(new L_D_LOG_COMMANDE
        {
            Commande = "P60",
            Date = new DateTime(2026, 2, 9, 12, 0, 0),
            Message = $"{reference.NumeroFichier} — OK",
            NumLingot = 0,
            OF = reference.OF!,
            Trace = true,
            User = InitiatingServer,
        });
        context.SaveChanges();
    }

    // Processes one Fichier with an ILogger backed by the real Serilog MSSqlServer sink, then flushes
    // the sink so ReadMqttLogs sees the row.
    private ImportResult RunWithSerilog(string fichierName, byte[] content)
    {
        MSSqlServerSinkOptions sinkOptions = new()
        {
            TableName = "Logs",
            AutoCreateSqlTable = false,
            BatchPostingLimit = 1,
            BatchPeriod = TimeSpan.FromMilliseconds(200),
        };

        Serilog.Core.Logger serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.MSSqlServer(fixture.MqttConnectionString, sinkOptions)
            .CreateLogger();

        try
        {
            SerilogLoggerFactory factory = new(serilog);
            ILogger<Kape22FichierProcessor> logger = factory.CreateLogger<Kape22FichierProcessor>();

            return new Kape22FichierProcessor(
                fixture.NewAscoLsiContext, Configuration(), Options(), WinterClock(), logger)
                .Import(fichierName, content);
        }
        finally
        {
            // Dispose flushes the periodic-batching sink.
            serilog.Dispose();
        }
    }

    private List<LogRow> ReadMqttLogs()
    {
        using SqlConnection connection = new(fixture.MqttConnectionString);
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT [Level], [Message] FROM dbo.Logs ORDER BY [Id];";
        command.CommandType = CommandType.Text;

        using SqlDataReader reader = command.ExecuteReader();
        List<LogRow> rows = [];
        while (reader.Read())
        {
            rows.Add(new LogRow(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return rows;
    }

    private sealed record LogRow(string Level, string Message);
}
