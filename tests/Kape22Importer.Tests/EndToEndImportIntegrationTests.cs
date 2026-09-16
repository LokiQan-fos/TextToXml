using System;
using System.Collections.Generic;
using System.Globalization;
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

// Story 3.6 / SM-2: the end-to-end proof. The ten P60/ sample Fichiers dropped in the inbox, one
// InboxScanner tick over the real Kape22FichierProcessor and the local SQL Server test instance
// (AR-12), and afterwards exactly ten coherent L_D_KAPE22 rows plus ten "— OK" L_D_LOG_COMMANDE rows,
// with every Fichier archived next to its normalized XML and nothing left in the inbox or processing/.
// Integration category: needs a reachable test instance and skips cleanly otherwise. Commit + reset
// regime (ResetData first), because SM-2 reads committed state back and the D22 anti-duplicate guard
// depends on it. Written test-first (CC-1). Vocabulary follows the PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class EndToEndImportIntegrationTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    // The inbox root folder key of the in-memory IFileSource.
    private const string InboxRoot = "";

    // A fixed summer instant: Paris is UTC+2, so the archive date folder is 2026/09.
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    private const string ArchiveDateFolder = "archive/2026/09";

    // The ten valid samples copied into fixtures/valid/ by Story 1.1.
    private static readonly string[] TenFichiers =
    [
        "P60_847_682_001", "P60_847_682_002", "P60_847_682_003", "P60_847_682_004", "P60_847_682_005",
        "P60_847_682_006", "P60_847_682_007", "P60_847_682_008", "P60_847_682_009", "P60_847_682_010",
    ];

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

    private InboxScanner Scanner(InMemoryFileSource source)
    {
        Kape22FichierProcessor processor = new(
            fixture.NewAscoLsiContext,
            Configuration(),
            Options(),
            new FixedClock(Now),
            NullLogger<Kape22FichierProcessor>.Instance);

        return new InboxScanner(source, processor, Options(), new FixedClock(Now), new RecordingLogger<InboxScanner>());
    }

    private void Ready()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
    }

    // SM-2: the ten samples import as ten L_D_KAPE22 rows whose visible business fields (OF, Coulee,
    // Client, Nuance) match what the pipeline reads from each Fichier, alongside ten "— OK"
    // L_D_LOG_COMMANDE rows. Every Fichier ends up archived with its .xml sidecar; the inbox and
    // processing/ are empty.
    [SkippableFact]
    [Trait("AC", "SM-2")]
    public void Tick_TenSampleFichiers_InsertTenCoherentRowsAndArchiveEveryFichier_Sm2()
    {
        Ready();

        InMemoryFileSource source = new();
        foreach ((string name, int index) in TenFichiers.Select((name, index) => (name, index)))
        {
            source.Add(InboxRoot, name, Content(name), Now.AddMinutes(-10 + index));
        }

        Scanner(source).RunTick();

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        List<L_D_KAPE22> rows = verify.Kape22Rows.AsNoTracking().ToList();
        Assert.Equal(10, rows.Count);

        List<L_D_LOG_COMMANDE> logRows = verify.LogCommandeRows.AsNoTracking().ToList();
        Assert.Equal(10, logRows.Count);

        foreach (string name in TenFichiers)
        {
            Visible expected = Expected(name, Content(name));
            L_D_KAPE22 row = Assert.Single(
                rows,
                candidate => candidate.NumeroFichier.Trim() == expected.NumeroFichier
                    && candidate.OF.Trim() == expected.OF);

            Assert.Equal(expected.Client, row.Client.Trim());
            Assert.Equal(expected.Coulee, row.Coulee.Trim());
            Assert.Equal(expected.Nuance, row.Nuance.Trim());

            // Ties this Fichier's own "— OK" row back to its NumeroFichier + OF, so a bug that logs the
            // wrong Fichier or drops one row while duplicating another cannot hide behind the count check.
            Assert.Single(
                logRows,
                candidate => candidate.OF.Trim() == expected.OF
                    && candidate.Message == expected.NumeroFichier + " — OK");
        }

        foreach (string name in TenFichiers)
        {
            Assert.True(source.Exists(ArchiveDateFolder, name), $"{name} must be archived.");
            Assert.True(source.Exists(ArchiveDateFolder, name + ".xml"), $"{name} must be archived with its .xml sidecar.");
        }

        Assert.Empty(source.Names(InboxRoot));
        Assert.Empty(source.Names(Options().ProcessingFolder));
    }

    // Story 4.6 (deferred-work.md, decimal-scale defect): the bytes actually fed to the scanner for a
    // given sample - the reference Fichier needs its Coulee corrected too (AC-FR20-3), every other
    // sample only needs its out-of-scale dimension Champs zeroed.
    private static byte[] Content(string fichierName) =>
        fichierName == ReferenceFichierName ? InsertableReferenceFichier() : InsertableFichier(fichierName);

    // Runs one Fichier through Converter and Kape22Mapper on its own, so the end-to-end row can be
    // cross-checked against what the pipeline reads rather than against a hard-coded expectation.
    private static Visible Expected(string fichierName, byte[] content)
    {
        ConversionResult conversion = Converter.Convert(content, EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, $"{fichierName} failed Step 1.");

        MapResult<L_D_KAPE22> map = new Kape22Mapper(new FixedClock(Now)).Map(conversion.Xml!, fichierName);
        Assert.True(map.Success, $"{fichierName} failed Step 2.");
        L_D_KAPE22 entity = map.Value!;

        return new Visible(
            entity.Client.Trim(),
            entity.Coulee.Trim(),
            entity.Nuance.Trim(),
            entity.NumeroFichier.Trim(),
            entity.OF.Trim());
    }

    // Properties are declared in alphabetical order (CC-4).
    private sealed record Visible(string Client, string Coulee, string Nuance, string NumeroFichier, string OF);
}
