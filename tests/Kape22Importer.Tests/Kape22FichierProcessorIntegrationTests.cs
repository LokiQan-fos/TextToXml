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

    private static IConfiguration Configuration(string? commande = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = commande ?? "P60",
                ["Import:InitiatingServer"] = InitiatingServer,
            })
            .Build();

    private Kape22FichierProcessor Processor(string? commande = null) =>
        new(fixture.NewAscoLsiContext, Configuration(commande), Options(), WinterClock(), NullLogger<Kape22FichierProcessor>.Instance);

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

        ImportResult result = Processor().Import(ReferenceFichierName, InsertableReferenceFichier());

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
        ImportResult clean = processor.Import(ReferenceFichierName, InsertableReferenceFichier());

        Assert.False(rejected.Success);
        Assert.True(clean.Success);
        Assert.NotNull(clean.InsertedId);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        List<string> messages = verify.LogCommandeRows.AsNoTracking().Select(row => row.Message).ToList();
        Assert.Contains(messages, message => message.Contains("REJETÉ"));
        Assert.Contains(messages, message => message.EndsWith("— OK"));
    }

    // AC-FR6-4 extended to ImportResult: when the mapper rejects a Fichier and the REJETÉ
    // L_D_LOG_COMMANDE insert then fails too (an over-long Commande from configuration), Import returns
    // the mapper rejection reason (RequiredFieldMissing, LineNumber 2) and the File-level
    // PersistenceError (LineNumber 0) as one list re-sorted by LineNumber - the File-level entry first -
    // not in the order Kape22Persister.PersistenceFailure produced them.
    [SkippableFact]
    [Trait("AC", "FR6-4")]
    public void Import_MapperRejectionThenLogRowInsertFails_ErrorsAreSortedByLineNumber_AcFr6_4()
    {
        Ready();

        ImportResult result = Processor(commande: new string('P', 100))
            .Import(ReferenceFichierName, BlankClientReferenceFichier());

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.RequiredFieldMissing);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.PersistenceError);
        Assert.Equal(
            result.Errors.Select(error => error.LineNumber).OrderBy(line => line),
            result.Errors.Select(error => error.LineNumber));
        Assert.Equal(ErrorCode.PersistenceError, result.Errors[0].Code);
    }

    // AC-FR13-5: a success ImportResult - Success true, InsertedId non-null, Errors empty (Warnings may
    // be present), NormalizedXml and XmlArchivePath non-null.
    [SkippableFact]
    [Trait("AC", "FR13-5")]
    public void Import_Success_ResultShape_AcFr13_5()
    {
        Ready();

        ImportResult result = Processor().Import(ReferenceFichierName, InsertableReferenceFichier());

        Assert.True(result.Success);
        Assert.NotNull(result.InsertedId);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.NormalizedXml);
        Assert.Equal(ExpectedXmlArchivePath, result.XmlArchivePath);
    }

    // Story 4.12 (AC-FR19-5): the processor loads the L_P_CONSIGNES_* snapshot from the Fichier's own
    // context, so seeded reference rows reach the persisted labels; an unseeded lookup gives "?" and a
    // computed type needs no reference row at all.
    [SkippableFact]
    [Trait("AC", "FR19-5")]
    public void Import_SeededReferenceRows_PersistResolvedLibelles_AcFr19_5()
    {
        Ready();
        using (AscoLsiDbContext seed = fixture.NewAscoLsiContext())
        {
            seed.Database.ExecuteSqlRaw(
                """
                INSERT INTO dbo.L_P_CONSIGNES_LINGOT (Section, Consignes, CodeConsigne, Libelle, DateMaj)
                VALUES (N'LA1', 7, N'0', N'Pas de scarfing', '2020-01-01');
                INSERT INTO dbo.L_P_CONSIGNES_REFROIDISSEMENT (Code, Libelle, DateMaj)
                VALUES (N'00', N'Refroidissement à l''air', NULL);
                """);
        }

        ImportResult result = Processor().Import(ReferenceFichierName, InsertableReferenceFichier());
        Assert.True(result.Success);

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        List<L_D_CONSIGNES> rows = [.. verify.ConsignesRows.AsNoTracking()];
        string? Libelle(string codeOperation, int type) =>
            Assert.Single(rows, row => row.CodeOperation.Trim() == codeOperation && row.TypeConsigne == type).LibelleConsigne;

        Assert.Equal("Pas de scarfing", Libelle("LA1", 7));
        Assert.Equal("Refroidissement à l'air", Libelle("XA1", 21));
        Assert.Equal("?", Libelle("LA1", 9));
        Assert.Equal("1250", Libelle("PC1", 10));

        // Type 22 needs the OF and Pits inputs the bundle mapper forwards; with empty degassing tables it
        // resolves to "0", while a dropped input would give "?".
        Assert.Equal("0", Libelle("XA1", 22));
        Assert.All(rows, row => Assert.NotNull(row.LibelleConsigne));
    }
}
