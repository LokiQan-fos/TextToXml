using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using AscoLsiJournal;
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
// the cause stays readable through the double-journal circuit (L_D_LOG_COMMANDE and MQTTnetServices.Logs
// for all three - the SQL failure's journal row since Story 6.1, FR-24). This extends SM-2
// (EndToEndImportIntegrationTests, AC-FR21-4) with the rollback side of the same real-pipeline proof;
// only TransactionalPersistenceTests proved it at the Persister-unit level before this story. Reuses
// DoubleJournalIntegrationTests' Serilog/MSSqlServer wiring (RunWithSerilog/ReadMqttLogs) and
// TransactionalPersistenceTests' "list every *Rows.AsNoTracking(), assert empty" rollback pattern (lines
// 127-141, 291-330). No production code changes; test-only (spec-4-7). AR-12: Integration category,
// skips cleanly without a reachable local SQL Server test instance. Commit + reset regime (ResetData/ResetMqttLogs first) since the rows are read
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

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
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
        L_D_LOG_COMMANDE log = Assert.Single(fixture.LogRows());
        Assert.Contains("REJETÉ", log.Message);

        AssertNoDispatchRows(verify);
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
        L_D_LOG_COMMANDE log = Assert.Single(fixture.LogRows());
        Assert.Contains("REJETÉ", log.Message);

        AssertNoDispatchRows(verify);
    }

    // AC-FR21-2 / AC-FR21-5 / AC-FR24-3: a genuine SQL failure on the one SaveChanges - an orphan
    // L_D_CONSIGNES row already on file for this OF, with no L_D_KAPE22 row so the D22 guard lets the
    // Fichier through and no L_D_ORDRE_FABRICATION row so the D34 replace (Story 6.10) does not apply -
    // rolls back L_D_KAPE22 and every downstream entity; the cause is readable in
    // MQTTnetServices.Logs and, since Story 6.1, in a REJETÉ journal row written after the rollback and
    // naming the table. Before Epic 6 an over-long Commande forced this failure through the log row
    // itself; that row now lives outside the transaction (AC-FR24-5, TransactionalPersistenceTests).
    [SkippableFact]
    [Trait("AC", "FR21-5")]
    [Trait("AC", "FR24-3")]
    public void Import_SimulatedSqlFailure_RollsBackEveryDispatchTableAndJournalsTheCause_AcFr21_5()
    {
        Ready();

        // Story 6.9: the reference Fichier is cold, so its Coulee is on file.
        SeedCoulees(fixture.NewAscoLsiContext, ReferenceFichierName);
        using (AscoLsiDbContext seed = fixture.NewAscoLsiContext())
        {
            seed.ConsignesRows.Add(MapReferenceBundle().Consignes[0]);
            seed.SaveChanges();
        }

        ImportResult result = RunWithSerilog(ReferenceFichierName, InsertableReferenceFichier());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        L_D_LOG_COMMANDE log = Assert.Single(fixture.LogRows());
        Assert.Contains("REJETÉ", log.Message);
        Assert.Contains("L_D_CONSIGNES", log.Message, StringComparison.Ordinal);

        // Every dispatch table stays empty except L_D_CONSIGNES, which keeps only the seeded row.
        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking());

        // Story 6.9: L_D_COULEE holds only the seeded reference Coulee, unmodified; the rolled-back
        // transaction added none.
        AssertOnlyTheSeededReferenceCoulee(verify.CouleeRows);
        Assert.Empty(verify.SectionChargeChutageRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeDecoupeRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeLingotRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePitsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargePoidsMetriqueRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeRefroidissoirsRows.AsNoTracking());
        Assert.Empty(verify.SectionChargeSvtRows.AsNoTracking());
        Assert.Single(verify.ConsignesRows.AsNoTracking());
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

        // Story 6.9: the untouched reference Fichier is cold too, so its external Coulee is on file.
        SeedCoulee(fixture.NewAscoLsiContext, MapFichier(content, ReferenceFichierName));

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Information", row.Level);
        Assert.Contains("[Kape22Importer][ImportSucceeded]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.EndsWith("— OK", Assert.Single(fixture.LogRows()).Message);
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
        L_D_LOG_COMMANDE log = Assert.Single(fixture.LogRows());
        Assert.Contains("REJETÉ", log.Message);

        AssertNoDispatchRows(verify);
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

        // Story 6.9: the reference Fichier is cold, so its Coulee is on file and the collision is what
        // rejects it.
        SeedCoulee(fixture.NewAscoLsiContext, MapFichier(fichier, ReferenceFichierName));

        ImportResult result = RunWithSerilog(ReferenceFichierName, content);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.BusinessRuleViolation);

        LogRow row = Assert.Single(ReadMqttLogs());
        Assert.Equal("Error", row.Level);
        Assert.Contains("[Kape22Importer][ImportRejected]", row.Message);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_LOG_COMMANDE log = Assert.Single(fixture.LogRows());
        Assert.Contains("REJETÉ", log.Message);

        AssertNoDispatchRows(verify, referenceCouleeSeeded: true);
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
        fixture.ResetMqttLogs();
    }

    // The "list every *Rows.AsNoTracking(), assert empty" pattern from TransactionalPersistenceTests.cs,
    // covering all 11 AscoLSI dispatch tables (L_D_KAPE22 + the 10 Story 4.1 downstream tables) - never
    // L_D_LOG_COMMANDE, which is asserted separately per scenario since every cause records a REJETÉ row
    // there on purpose. Story 6.9: when a test seeded the reference Coulee, L_D_COULEE must hold exactly
    // that row, unmodified, instead of nothing.
    private static void AssertNoDispatchRows(AscoLsiDbContext verify, bool referenceCouleeSeeded = false)
    {
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.OrdreFabricationRows.AsNoTracking());
        if (referenceCouleeSeeded)
        {
            AssertOnlyTheSeededReferenceCoulee(verify.CouleeRows);
        }
        else
        {
            Assert.Empty(verify.CouleeRows.AsNoTracking());
        }

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
    // sink so ReadMqttLogs sees the row (DoubleJournalIntegrationTests' own RunWithSerilog).
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
                fixture.NewAscoLsiContext, Configuration(), fixture.NewJournal(), Options(), WinterClock(), logger)
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
