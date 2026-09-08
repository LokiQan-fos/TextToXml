using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using Xunit.Sdk;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.2 / FR-13: Kape22FichierProcessor is the real IFichierProcessor. Per Fichier it runs a strict
// order - raw bytes -> Converter.Convert -> (on success) capture the normalized XML -> Kape22Mapper.Map
// -> Kape22Persister.Persist over a fresh context - and returns an ImportResult reflecting exactly the
// step reached. These Category=Unit tests cover the whole pipeline: the Converter-failure branch runs
// with a throwing context factory, and the mapper-failure and success branches run over an EF in-memory
// AscoLsiDbContext. The transaction-boundary and column-length assertions stay on SQL Server in
// Kape22FichierProcessorIntegrationTests (AR-12). Written test-first (CC-1). Vocabulary follows the PRD
// glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class Kape22FichierProcessorTests
{
    // WinterClock is 2026-02-10 08:00 UTC; Paris winter is UTC+1, so the archive date folder is 2026/02.
    private const string ExpectedXmlArchivePath = "archive/2026/02/" + ReferenceFichierName + ".xml";

    private static ImportOptions Options() => new()
    {
        ArchiveFolder = "archive",
        ErrorFolder = "error",
        InboxPath = "inbox",
        InitiatingServer = "AFS017",
        PollingInterval = TimeSpan.FromSeconds(30),
        ProcessingFolder = "processing",
        RetentionDays = 30,
    };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
                ["Import:InitiatingServer"] = "AFS017",
            })
            .Build();

    // A context factory that fails the test if persistence is ever reached.
    private static Func<AscoLsiDbContext> PersistenceMustNotBeReached() =>
        () => throw new XunitException("Persistence was reached; the Converter failure should have stopped the pipeline.");

    private static Kape22FichierProcessor Processor(Func<AscoLsiDbContext> newContext) =>
        new(newContext, Configuration(), Options(), WinterClock(), NullLogger<Kape22FichierProcessor>.Instance);

    // AC-FR13-2: Converter.Convert fails -> no normalized XML is produced, Kape22Mapper and
    // Kape22Persister are never reached, and ImportResult.Errors carries the Step 1 error.
    [Fact]
    [Trait("AC", "FR13-2")]
    public void Import_ConverterFails_ProducesNoXmlAndNeverMapsOrPersists_AcFr13_2()
    {
        ImportResult result = Processor(PersistenceMustNotBeReached()).Import(ReferenceFichierName, []);

        Assert.False(result.Success);
        Assert.Null(result.NormalizedXml);
        Assert.Null(result.InsertedId);
        Assert.Null(result.XmlArchivePath);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.EmptyFile, error.Code);
    }

    // AC-FR13-2: the same Converter failure reaches InboxScanner through the IFichierProcessor.Process
    // adapter as a rejected FichierProcessingResult with no normalized XML.
    [Fact]
    [Trait("AC", "FR13-2")]
    public void Process_ConverterFailure_AdaptsToRejectedResultWithNoXml_AcFr13_2()
    {
        FichierProcessingResult result = Processor(PersistenceMustNotBeReached()).Process(ReferenceFichierName, []);

        Assert.False(result.Success);
        Assert.Null(result.NormalizedXml);
        Assert.NotEmpty(result.Errors);
    }

    // AC-FR13-1: a clean Fichier runs Converter -> XML capture -> Kape22Mapper -> Kape22Persister in
    // order; the committed row plus the non-null InsertedId / NormalizedXml / XmlArchivePath prove every
    // stage ran.
    [Fact]
    [Trait("AC", "FR13-1")]
    public void Import_CleanFichier_RunsThroughToTheInsert_AcFr13_1()
    {
        InMemoryContextFactory contexts = new();

        ImportResult result = Processor(contexts.Next).Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);
        Assert.NotNull(result.NormalizedXml);
        Assert.Equal(ExpectedXmlArchivePath, result.XmlArchivePath);

        using AscoLsiDbContext verify = contexts.Reader();
        L_D_KAPE22 inserted = Assert.Single(verify.Kape22Rows);
        Assert.Equal(inserted.Id, result.InsertedId);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows);
        Assert.EndsWith("— OK", log.Message);
    }

    // AC-FR13-3: Converter succeeds, Kape22Mapper rejects -> no L_D_KAPE22 row, the normalized XML is
    // kept on the result so <nom>.xml can ride into error/, and Kape22Persister still writes the one
    // REJETÉ L_D_LOG_COMMANDE row (the OF is readable).
    [Fact]
    [Trait("AC", "FR13-3")]
    public void Import_MapperFailure_KeepsXmlAndWritesOnlyTheRejectedLogRow_AcFr13_3()
    {
        InMemoryContextFactory contexts = new();

        ImportResult result = Processor(contexts.Next).Import(ReferenceFichierName, BlankClientReferenceFichier());

        Assert.False(result.Success);
        Assert.NotNull(result.NormalizedXml);
        Assert.Null(result.InsertedId);
        Assert.Null(result.XmlArchivePath);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.RequiredFieldMissing);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows);
        Assert.Contains("REJETÉ", log.Message);
    }

    // AC-FR13-3: a rejected Fichier keeps both its Step 1 Segment warnings and the mapper's FR-10
    // coherence warnings on the result, even though Kape22Persister.PersistRejected drops the mapper
    // Warnings on its way through. The name "P60_999_682_001" diverges from the Header roulette, so the
    // mapper raises a FileNameMismatch warning while the blank Client still rejects the Fichier.
    [Fact]
    [Trait("AC", "FR13-3")]
    public void Import_RejectedFichierWithCoherenceWarning_KeepsThatWarning_AcFr13_3()
    {
        InMemoryContextFactory contexts = new();

        ImportResult result = Processor(contexts.Next).Import("P60_999_682_001", BlankClientReferenceFichier());

        Assert.False(result.Success);
        Assert.Contains(result.Warnings, warning => warning.Code == ErrorCode.FileNameMismatch);
    }

    // AC-FR13-4: each Import call builds its own AscoLsiDbContext, so one Fichier's transaction and EF
    // change tracker never touch another's. Two calls hand out two distinct instances.
    [Fact]
    [Trait("AC", "FR13-4")]
    public void Import_EachCall_BuildsItsOwnContext_AcFr13_4()
    {
        InMemoryContextFactory contexts = new();
        Kape22FichierProcessor processor = Processor(contexts.Next);

        processor.Import(ReferenceFichierName, BlankClientReferenceFichier());
        processor.Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.Equal(2, contexts.Handed.Count);
        Assert.NotSame(contexts.Handed[0], contexts.Handed[1]);
    }

    // AC-FR13-4: a rejected Fichier processed just before a clean one never touches the clean import.
    // After the pair, exactly the clean Fichier's L_D_KAPE22 row is committed.
    [Fact]
    [Trait("AC", "FR13-4")]
    public void Import_RejectedFichierBeforeACleanOne_LeavesTheCleanImportIntact_AcFr13_4()
    {
        InMemoryContextFactory contexts = new();
        Kape22FichierProcessor processor = Processor(contexts.Next);

        ImportResult rejected = processor.Import(ReferenceFichierName, BlankClientReferenceFichier());
        ImportResult clean = processor.Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.False(rejected.Success);
        Assert.True(clean.Success);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Single(verify.Kape22Rows);
        List<string> messages = verify.LogCommandeRows.Select(row => row.Message).ToList();
        Assert.Contains(messages, message => message.Contains("REJETÉ"));
        Assert.Contains(messages, message => message.EndsWith("— OK"));
    }

    // AC-FR13-5: a success ImportResult - Success true, InsertedId non-null, Errors empty (Warnings may
    // be present), NormalizedXml and XmlArchivePath non-null.
    [Fact]
    [Trait("AC", "FR13-5")]
    public void Import_Success_ResultShape_AcFr13_5()
    {
        InMemoryContextFactory contexts = new();

        ImportResult result = Processor(contexts.Next).Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.NormalizedXml);
        Assert.Equal(ExpectedXmlArchivePath, result.XmlArchivePath);
    }
}
