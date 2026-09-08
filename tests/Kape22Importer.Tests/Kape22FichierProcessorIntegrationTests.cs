using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.2 / FR-13: the database-backed half of Kape22FichierProcessor - the strict order carried all
// the way to the L_D_KAPE22 insert (AC-FR13-1), per-Fichier EF scope and transaction isolation
// (AC-FR13-4), and the success ImportResult shape (AC-FR13-5). Integration category (AR-12): needs a
// reachable local SQL Server test instance and skips cleanly otherwise. Every test runs in the
// commit + reset regime (ResetData first) because the L_D_KAPE22 / L_D_LOG_COMMANDE rows and the D22
// anti-duplicate guard depend on committed state. Written test-first (CC-1): red until
// Kape22FichierProcessor.Import ships. Vocabulary follows the PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class Kape22FichierProcessorIntegrationTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    // WinterClock is 2026-02-10 08:00 UTC; Paris winter is UTC+1, so the archive date folder is 2026/02.
    private const string ExpectedXmlArchivePath = "archive/2026/02/" + ReferenceFichierName + ".xml";

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

    private Kape22FichierProcessor Processor() =>
        new(fixture.NewAscoLsiContext, Configuration(), Options(), WinterClock());

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
    }

    // AC-FR13-1: on a clean Fichier the pipeline runs through to the L_D_KAPE22 + "<NumeroFichier> — OK"
    // L_D_LOG_COMMANDE insert. This asserts the end state - committed rows plus non-null InsertedId,
    // NormalizedXml and XmlArchivePath - which is only reachable when Converter, the XML capture,
    // Kape22Mapper and Kape22Persister have each run. The literal call ordering is covered by the
    // deferred-work note rather than a spy.
    [SkippableFact]
    [Trait("AC", "FR13-1")]
    public void Import_CleanFichier_RunsThroughToTheInsertWithAFullResult_AcFr13_1()
    {
        Ready();

        ImportResult result = Processor().Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);
        Assert.NotNull(result.NormalizedXml);
        Assert.Equal(ExpectedXmlArchivePath, result.XmlArchivePath);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_KAPE22 inserted = Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.Equal(inserted.Id, result.InsertedId);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.EndsWith("— OK", log.Message);
    }

    // AC-FR13-2: Converter.Convert fails -> nothing is written to AscoLSI at all (no L_D_KAPE22 row, no
    // L_D_LOG_COMMANDE row), and the result carries the Step 1 error with no normalized XML.
    [SkippableFact]
    [Trait("AC", "FR13-2")]
    public void Import_ConverterFailure_WritesNothingToAscoLsi_AcFr13_2()
    {
        Ready();

        ImportResult result = Processor().Import(ReferenceFichierName, []);

        Assert.False(result.Success);
        Assert.Null(result.NormalizedXml);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.EmptyFile);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        Assert.Empty(verify.LogCommandeRows.AsNoTracking());
    }

    // AC-FR13-3: Converter succeeds, Kape22Mapper rejects -> no L_D_KAPE22 row, one "REJETÉ"
    // L_D_LOG_COMMANDE row (the OF is still readable), and the normalized XML is kept on the result so
    // <nom>.xml can accompany the Fichier into error/.
    [SkippableFact]
    [Trait("AC", "FR13-3")]
    public void Import_MapperFailure_KeepsXmlAndWritesOnlyTheRejectedLogRow_AcFr13_3()
    {
        Ready();

        ImportResult result = Processor().Import(ReferenceFichierName, BlankClientReferenceFichier());

        Assert.False(result.Success);
        Assert.NotNull(result.NormalizedXml);
        Assert.Null(result.InsertedId);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.RequiredFieldMissing);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Empty(verify.Kape22Rows.AsNoTracking());
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows.AsNoTracking());
        Assert.Contains("REJETÉ", log.Message);
    }

    // AC-FR13-4: a rejected Fichier processed just before a clean one never touches the clean Fichier's
    // import - each Import call gets its own AscoLsiDbContext and its own transaction. After the pair,
    // exactly the clean Fichier's L_D_KAPE22 row is committed, with an OK log row and a REJETÉ log row
    // side by side.
    [SkippableFact]
    [Trait("AC", "FR13-4")]
    public void Import_RejectedFichierBeforeACleanOne_LeavesTheCleanImportIntact_AcFr13_4()
    {
        Ready();
        Kape22FichierProcessor processor = Processor();

        ImportResult rejected = processor.Import(ReferenceFichierName, BlankClientReferenceFichier());
        ImportResult clean = processor.Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.False(rejected.Success);
        Assert.True(clean.Success);
        Assert.NotNull(clean.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        List<string> messages = verify.LogCommandeRows.AsNoTracking().Select(row => row.Message).ToList();
        Assert.Contains(messages, message => message.Contains("REJETÉ"));
        Assert.Contains(messages, message => message.EndsWith("— OK"));
    }

    // AC-FR13-5: a success ImportResult - Success true, InsertedId non-null, Errors empty (Warnings may
    // be present), NormalizedXml and XmlArchivePath non-null.
    [SkippableFact]
    [Trait("AC", "FR13-5")]
    public void Import_Success_ResultShape_AcFr13_5()
    {
        Ready();

        ImportResult result = Processor().Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.NormalizedXml);
        Assert.Equal(ExpectedXmlArchivePath, result.XmlArchivePath);
    }
}
