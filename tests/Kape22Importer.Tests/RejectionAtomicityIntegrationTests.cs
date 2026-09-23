using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
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

// Story 4.7 (FR-21, AC-FR21-5): AD-1 atomicity on the rejection path, one dedicated E2E fixture per
// cause - cold Coulee missing, inconsistent ingot/furnace distribution, a simulated SQL failure - each
// proving zero rows land in any of the 11 AscoLSI dispatch tables (L_D_KAPE22 + 10 downstream) and that
// the cause stays readable
// through the double-journal circuit (L_D_LOG_COMMANDE for the two business rejections,
// MQTTnetServices.Logs for all three). This extends SM-2 (EndToEndImportIntegrationTests, AC-FR21-4)
// with the rollback side of the same real-pipeline proof; only TransactionalPersistenceTests proved it at
// the Persister-unit level before this story. Reuses DoubleJournalIntegrationTests' Serilog/MSSqlServer
// wiring (RunWithSerilog/ReadMqttLogs) and TransactionalPersistenceTests' "list every
// *Rows.AsNoTracking(), assert empty" rollback pattern (lines 127-141, 291-330). No production code
// changes; test-only (spec-4-7). AR-12: Integration category, skips cleanly without a reachable local SQL
// Server test instance. Commit + reset regime (ResetData/ResetMqttLogs first) since the rows are read
// back after the transaction commits or rolls back.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class RejectionAtomicityIntegrationTests(SqlServerIntegrationFixture fixture)
{
    // Detail-block Position/Size per Templates/P60.xml.
    private const int CodeConsignePitsPosition = 146;

    private const int CodeConsignePitsSize = 12;

    private const int CodeOpeChutagePosition = 241;

    private const int CodeOpeChutageSize = 3;

    private const int CodeOpeDecoupePosition = 284;

    private const int CodeOpeDecoupeSize = 3;

    private const int CodeOpePitsPosition = 125;

    private const int CodeOpePitsSize = 3;

    private const string InitiatingServer = "AFS017";

    private const int NombreLingotsFour1Position = 417;

    private const int NombreLingotsFour1Size = 2;

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

    private static IConfiguration Configuration(string? commande = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = commande ?? "P60",
                ["Import:InitiatingServer"] = InitiatingServer,
            })
            .Build();

    // AC-FR20-5 / AC-FR21-5: a cold Coulee (CodeConsignePits "1") whose L_D_COULEE row does not exist yet
    // is rejected before anything is added - zero rows in all 11 dispatch tables, a REJETÉ
    // L_D_LOG_COMMANDE row citing the missing Coulee, and an Error line in MQTTnetServices.Logs.
    [SkippableFact]
    [Trait("AC", "FR21-5")]
    public void Import_ColdCouleeMissing_LeavesAllElevenTablesEmptyWithReadableCause_AcFr21_5()
    {
        Ready();
        byte[] content = WithDetailChamp(
            InsertableReferenceFichier(), CodeConsignePitsPosition, CodeConsignePitsSize, "1");

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);

        AssertAllElevenTablesEmpty(verify);
    }

    // AC-FR20-2 / AC-FR21-5: the OF's two furnace ingot counts no longer sum to its own expected
    // NombreDemiProduit - zero rows in all 11 dispatch tables, a REJETÉ L_D_LOG_COMMANDE row citing the
    // mismatch, and an Error line in MQTTnetServices.Logs.
    [SkippableFact]
    [Trait("AC", "FR21-5")]
    public void Import_InconsistentIngotFurnaceDistribution_LeavesAllElevenTablesEmptyWithReadableCause_AcFr21_5()
    {
        Ready();
        byte[] content = WithDetailChamp(
            InsertableReferenceFichier(), NombreLingotsFour1Position, NombreLingotsFour1Size, "99");

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);

        AssertAllElevenTablesEmpty(verify);
    }

    // AC-FR11-3 / AC-FR21-2 / AC-FR21-5: an over-long Commande (configuration, not the Fichier) fails the
    // L_D_LOG_COMMANDE insert of the one SaveChanges that also stages L_D_KAPE22 and every downstream
    // entity - the whole transaction rolls back, so all 11 dispatch tables AND L_D_LOG_COMMANDE stay
    // empty; only MQTTnetServices.Logs carries the cause (precedent:
    // TransactionalPersistenceTests.Persist_BundleSuccess_LogRowInsertFails_RollsBackKape22Row_AcFr11_3).
    [SkippableFact]
    [Trait("AC", "FR21-5")]
    public void Import_SimulatedSqlFailure_LeavesAllElevenTablesAndLogCommandeEmptyWithReadableCause_AcFr21_5()
    {
        Ready();
        byte[] content = InsertableReferenceFichier();

        ImportResult result = RunWithSerilog(ReferenceFichierName, content, commande: new string('P', 100));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());

        AssertAllElevenTablesEmpty(verify);
    }

    // AC-FR20-3 removed 2026-09-22 (Kape22ImportBundleMapper.cs Design Notes): hot/cold and
    // internal/external Coulee provenance are independent - confirmed by the process owner after this
    // control rejected the untouched reference Fichier (hot enfournement, externally-cast Coulee
    // '165718', a real, legitimate combination). Was proven only at the Persister-unit level before Story
    // 4.7; now proves through the real Kape22FichierProcessor.Import pipeline that this exact case
    // succeeds rather than being rejected - the positive counterpart of every other test in this file.
    [SkippableFact]
    [Trait("AC", "FR20-3")]
    public void Import_HotExternalCoulee_SucceedsRatherThanRejects_AcFr20_3()
    {
        Ready();
        byte[] content = ReadValidFixture(ReferenceFichierName);

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Information", row.Level);
        Assert.Contains("[Kape22Importer][ImportSucceeded]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.EndsWith("— OK", Assert.Single(verify.LogCommandeRows.AsNoTracking()).Message);
    }

    // C-5: AC-FR20-4 - every OF needs an enfournement instruction (L_D_SECTIONCHARGE_PITS); a blank
    // CodeOpePits makes the section inapplicable, itself the violation. Zero rows in all 11 dispatch
    // tables, a REJETÉ L_D_LOG_COMMANDE row, and an Error line in MQTTnetServices.Logs.
    [SkippableFact]
    [Trait("AC", "FR20-4")]
    public void Import_MissingPitsInstruction_LeavesAllElevenTablesEmptyWithReadableCause_AcFr20_4()
    {
        Ready();
        byte[] content = WithDetailChamp(
            InsertableReferenceFichier(), CodeOpePitsPosition, CodeOpePitsSize, string.Empty);

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);

        AssertAllElevenTablesEmpty(verify);
    }

    // C-5: A-5 - two sections sharing the same CodeOperation for one OF (CodeOpeDecoupe forced onto
    // CodeOpeChutage's own raw value) is caught by the in-memory pre-check before ConsignesRows.AddRange,
    // instead of throwing an uncaught InvalidOperationException - proven only at the Persister-unit level
    // before this story (TransactionalPersistenceTests.Persist_ConsignesNaturalKeyCollision_...). Zero
    // rows in all 11 dispatch tables, a REJETÉ L_D_LOG_COMMANDE row, and an Error line in
    // MQTTnetServices.Logs, proven through the real Kape22FichierProcessor.Import pipeline this time.
    [SkippableFact]
    [Trait("AC", "A-5")]
    public void Import_ConsignesNaturalKeyCollision_LeavesAllElevenTablesEmptyWithReadableCause_A5()
    {
        Ready();
        byte[] fichier = InsertableReferenceFichier();
        string codeOpeChutage = ReadDetailChamp(fichier, CodeOpeChutagePosition, CodeOpeChutageSize);
        byte[] content = WithDetailChamp(fichier, CodeOpeDecoupePosition, CodeOpeDecoupeSize, codeOpeChutage);

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);

        AssertAllElevenTablesEmpty(verify);
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
        fixture.ResetMqttLogs();
    }

    // The "list every *Rows.AsNoTracking(), assert empty" pattern from TransactionalPersistenceTests.cs
    // (lines 127-141, 291-330), covering all 11 AscoLSI dispatch tables (L_D_KAPE22 + the 10 Story 4.1
    // downstream tables) - never L_D_LOG_COMMANDE, which is asserted separately per scenario since two of
    // the three causes commit a REJETÉ row there on purpose.
    private static void AssertAllElevenTablesEmpty(AscoLsiDbContext verify)
    {
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
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

    // Processes one Fichier with an ILogger backed by the real Serilog MSSqlServer sink, then flushes the
    // sink so ReadMqttLogs sees the row (DoubleJournalIntegrationTests' own RunWithSerilog, parameterized
    // here with an optional Commande override for the simulated-SQL-failure fixture).
    private ImportResult RunWithSerilog(string fichierName, byte[] content, string? commande = null)
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
                fixture.NewAscoLsiContext, Configuration(commande), Options(), WinterClock(), logger)
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
