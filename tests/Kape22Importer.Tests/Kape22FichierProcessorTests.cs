using System;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using Xunit.Sdk;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.2 / FR-13: Kape22FichierProcessor is the real IFichierProcessor. Per Fichier it runs a strict
// order - raw bytes -> Converter.Convert -> (on success) capture the normalized XML -> Kape22Mapper.Map
// -> Kape22Persister.Persist over a fresh context - and returns an ImportResult reflecting exactly the
// step reached. Only the Converter-failure branch is genuinely database-free (the pipeline stops before
// any MapResult exists, so the persister is never constructed); it is covered here. The Mapper-failure
// and success branches both reach Kape22Persister - which needs a real SQL Server (AR-12) - and are
// covered in Kape22FichierProcessorIntegrationTests. Written test-first (CC-1). Vocabulary follows the
// PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class Kape22FichierProcessorTests
{
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

    // A context factory that fails the test if persistence is ever reached.
    private static Func<AscoLsiDbContext> PersistenceMustNotBeReached() =>
        () => throw new XunitException("Persistence was reached; the Converter failure should have stopped the pipeline.");

    private static Kape22FichierProcessor Processor(Func<AscoLsiDbContext> newContext) =>
        new(newContext, new ConfigurationBuilder().Build(), Options(), WinterClock());

    // AC-FR13-2: Converter.Convert fails -> no normalized XML is produced, Kape22Mapper and
    // Kape22Persister are never reached, and ImportResult.Errors carries the Step 1 error.
    [Fact]
    [Trait("AC", "FR13-2")]
    public void Import_ConverterFails_ProducesNoXmlAndNeverMapsOrPersists_AcFr13_2()
    {
        ImportResult result = Processor(PersistenceMustNotBeReached()).Import("P60_847_682_001", []);

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
        FichierProcessingResult result = Processor(PersistenceMustNotBeReached()).Process("P60_847_682_001", []);

        Assert.False(result.Success);
        Assert.Null(result.NormalizedXml);
        Assert.NotEmpty(result.Errors);
    }
}
