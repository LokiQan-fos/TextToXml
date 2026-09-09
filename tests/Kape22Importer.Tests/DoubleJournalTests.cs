using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.3 (FR-14): the double logging Kape22FichierProcessor runs after every Fichier - one
// MQTTnetServices.Logs line, always, through ILogger (Serilog sink in production), plus, via
// Kape22Persister, one L_D_LOG_COMMANDE line when the OF is readable. These Category=Unit tests capture
// the ILogger side with a RecordingLogger over an EF in-memory AscoLsiDbContext: the level and the
// "[Kape22Importer][<Event>] : ..." shape for success (AC-FR14-1), rejection with a readable OF
// (AC-FR14-2), structural rejection with no readable OF (AC-FR14-3), an import carrying coherence
// Warnings (AC-FR14-8) and the already-imported skip, the best-effort contract (a throwing Logs sink
// never blocks the L_D_KAPE22 insert, AC-FR14-7), and AC-FR6-4 extended to ImportResult (Errors and
// Warnings each sorted by LineNumber). The real Serilog.Sinks.MSSqlServer round-trip is
// DoubleJournalIntegrationTests (AR-12). Written test-first (CC-1). Vocabulary follows the PRD glossary
// (CC-5).
[Trait("Category", TestCategory.Unit)]
public class DoubleJournalTests
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

    private static Kape22FichierProcessor Processor(
        Func<AscoLsiDbContext> newContext, ILogger<Kape22FichierProcessor> logger) =>
        new(newContext, Configuration(), Options(), WinterClock(), logger);

    // AC-FR14-1: a success writes exactly one Logs line, Information, prefixed
    // "[Kape22Importer][ImportSucceeded] :" and carrying the file name, NumeroFichier, OF, InsertedId,
    // the Detail line count and the elapsed time - and Kape22Persister still commits the "— OK"
    // L_D_LOG_COMMANDE row.
    [Fact]
    [Trait("AC", "FR14-1")]
    public void Import_Success_WritesOneInformationLogLineAndTheOkLogCommandeRow_AcFr14_1()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new();

        ImportResult result = Processor(contexts.Next, logger)
            .Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.StartsWith("[Kape22Importer][ImportSucceeded] :", entry.Message);
        Assert.Contains(ReferenceFichierName, entry.Message);
        Assert.Contains($"NumeroFichier={MapReferenceFichier().NumeroFichier}", entry.Message);
        Assert.Contains($"InsertedId={result.InsertedId}", entry.Message);
        Assert.Contains("OF=", entry.Message);
        Assert.Contains("Lignes=1", entry.Message);
        Assert.Matches(new Regex(@"Durée=\d+ ms"), entry.Message);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Single(verify.Kape22Rows);
        Assert.EndsWith("— OK", Assert.Single(verify.LogCommandeRows).Message);
    }

    // AC-FR14-2: a rejection with a readable OF writes one Logs line, Error, prefixed
    // "[Kape22Importer][ImportRejected] :" and listing every Error message; Kape22Persister writes the
    // matching "<NumeroFichier> — REJETÉ" L_D_LOG_COMMANDE row.
    [Fact]
    [Trait("AC", "FR14-2")]
    public void Import_RejectionWithReadableOf_WritesOneErrorLogLineListingEveryError_AcFr14_2()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new();

        ImportResult result = Processor(contexts.Next, logger)
            .Import(ReferenceFichierName, BlankClientReferenceFichier());

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.StartsWith("[Kape22Importer][ImportRejected] :", entry.Message);
        Assert.All(result.Errors, error => Assert.Contains(error.Message, entry.Message));

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Contains("REJETÉ", Assert.Single(verify.LogCommandeRows).Message);
    }

    // AC-FR14-3 (D15): a structural rejection - Converter.Convert fails, the OF is never read - still
    // writes the Logs line (Error), and no L_D_LOG_COMMANDE row at all.
    [Fact]
    [Trait("AC", "FR14-3")]
    public void Import_StructuralRejectionNoReadableOf_WritesOnlyTheErrorLogLine_AcFr14_3()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new();

        ImportResult result = Processor(contexts.Next, logger).Import(ReferenceFichierName, []);

        Assert.False(result.Success);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.StartsWith("[Kape22Importer][ImportRejected] :", entry.Message);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.LogCommandeRows);
    }

    // AC-FR14-8: an imported Fichier carrying coherence Warnings gets a second Logs line, Warning,
    // prefixed "[Kape22Importer][CoherenceWarnings] :" and naming each warning code, on top of the
    // success line. Two divergences are seeded at once so the line is shown to list every warning, not
    // just the first: the name "P60_999_682_001" diverges from the Header roulette (FileNameMismatch)
    // and Footer.Records "00009" is not 3 (InterBlockMismatch), while the clean bytes still import.
    [Fact]
    [Trait("AC", "FR14-8")]
    public void Import_SuccessWithCoherenceWarnings_AddsAWarningLogLineNamingEachWarning_AcFr14_8()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new();
        byte[] footerRecordsNotThree = WithText(ReadValidFixture(ReferenceFichierName), "00003", "00009");

        ImportResult result = Processor(contexts.Next, logger)
            .Import("P60_999_682_001", footerRecordsNotThree);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, warning => warning.Code == ErrorCode.FileNameMismatch);
        Assert.Contains(result.Warnings, warning => warning.Code == ErrorCode.InterBlockMismatch);

        Assert.Equal(LogLevel.Information, Assert.Single(logger.AtLevel(LogLevel.Information)).Level);
        var warning = Assert.Single(logger.AtLevel(LogLevel.Warning));
        Assert.StartsWith("[Kape22Importer][CoherenceWarnings] :", warning.Message);
        Assert.Contains(nameof(ErrorCode.FileNameMismatch), warning.Message);
        Assert.Contains(nameof(ErrorCode.InterBlockMismatch), warning.Message);
    }

    // The explicit anti-duplicate discriminator (D22, reconciliation A-3): a Fichier whose D22 key
    // already carries a committed "— OK" row comes back with AlreadyImported == true (not just the
    // structural Success && InsertedId == null shape), and the Logs line is a Warning prefixed
    // "[Kape22Importer][AlreadyImported] :".
    [Fact]
    [Trait("AC", "FR11-6")]
    public void Import_FichierAlreadyImported_SetsAlreadyImportedAndLogsAWarning_AcFr11_6()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new();
        SeedOkLogRow(contexts);

        ImportResult result = Processor(contexts.Next, logger)
            .Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.True(result.AlreadyImported);
        Assert.Null(result.InsertedId);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("[Kape22Importer][AlreadyImported] :", entry.Message);
    }

    // AC-FR14-7: the L_D_KAPE22 insert has already committed by the time the Logs line is written, so a
    // Logs sink that throws must not turn a successful import into a failure or lose the row.
    [Fact]
    [Trait("AC", "FR14-7")]
    public void Import_LogsSinkThrows_DoesNotPreventTheInsert_AcFr14_7()
    {
        InMemoryContextFactory contexts = new();
        RecordingLogger<Kape22FichierProcessor> logger = new() { Throw = true };

        ImportResult result = Processor(contexts.Next, logger)
            .Import(ReferenceFichierName, ReadValidFixture(ReferenceFichierName));

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Single(verify.Kape22Rows);
        Assert.EndsWith("— OK", Assert.Single(verify.LogCommandeRows).Message);
    }

    // AC-FR6-4 extended to ImportResult: Kape22FichierProcessor.Import concatenates conversion.Warnings
    // and map.Warnings, then re-sorts by LineNumber. Footer.Records != 3 raises an InterBlockMismatch at
    // the Footer Ligne (LineNumber 3); the name "P60_999_682_001" raises a FileNameMismatch at File
    // level (LineNumber 0). CoherenceChecker produces them in [3, 0] order; the result must expose them
    // 0 first, ascending, as one independent list.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void Import_ConcatenatedWarnings_AreSortedByLineNumberOnTheResult_AcFr6_4()
    {
        InMemoryContextFactory contexts = new();
        byte[] footerRecordsNotThree = WithText(ReadValidFixture(ReferenceFichierName), "00003", "00009");

        ImportResult result = Processor(contexts.Next, new RecordingLogger<Kape22FichierProcessor>())
            .Import("P60_999_682_001", footerRecordsNotThree);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 0);
        Assert.Contains(result.Warnings, warning => warning.LineNumber == 3);
        Assert.Equal(
            result.Warnings.Select(warning => warning.LineNumber).OrderBy(line => line),
            result.Warnings.Select(warning => warning.LineNumber));
    }

    // AC-FR6-4: SortedByLine is a stable sort - File-level entries (LineNumber 0) move to the front
    // while entries sharing a LineNumber keep the order the pipeline produced them in.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void SortedByLine_OrdersByLineNumberAscendingAndIsStable_AcFr6_4()
    {
        ConversionError footer = new() { Block = Block.Footer, LineNumber = 3, Message = "footer" };
        ConversionError file = new() { Block = Block.File, LineNumber = 0, Message = "file" };
        ConversionError detailA = new() { Block = Block.Detail, LineNumber = 2, Message = "detail-a" };
        ConversionError detailB = new() { Block = Block.Detail, LineNumber = 2, Message = "detail-b" };

        IReadOnlyList<ConversionError> sorted =
            Kape22FichierProcessor.SortedByLine([footer, detailA, file, detailB]);

        Assert.Equal(["file", "detail-a", "detail-b", "footer"], sorted.Select(entry => entry.Message));
    }

    // Seeds the committed "<NumeroFichier> — OK" L_D_LOG_COMMANDE row the D22 guard keys on, matching
    // exactly what Kape22Persister.OkLogRowExists compares (untrimmed OF, "<NumeroFichier> — OK").
    private static void SeedOkLogRow(InMemoryContextFactory contexts)
    {
        MapResult<L_D_KAPE22> reference = MapReferenceFichier();
        using AscoLsiDbContext context = contexts.Next();
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
}
