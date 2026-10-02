using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.6 / NFR-1 and NFR-2: the throughput guards. NFR-1 - one Fichier end to end (read, convert,
// map, insert, archive) stays under 200 ms; NFR-2 - a tick of 500 Fichiers finishes under 30 s. Both
// exclude FTP and SQL latency by design (PRD section 4.4), so the pipeline runs over the in-memory
// IFileSource and EF InMemory: what is measured is the library work, not the network. The ceilings are
// deliberately loose regression guards, not the budgets themselves, so a loaded CI runner does not
// flake them while a gross regression still trips. Written test-first (CC-1). Vocabulary follows the
// PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class EndToEndPerformanceTests
{
    private const string InitiatingServer = "AFS017";

    // The inbox root folder key of the in-memory IFileSource.
    private const string InboxRoot = "";

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    // The ten valid samples copied into fixtures/valid/ by Story 1.1.
    private static readonly string[] TenFichiers =
    [
        "P60_847_682_001", "P60_847_682_002", "P60_847_682_003", "P60_847_682_004", "P60_847_682_005",
        "P60_847_682_006", "P60_847_682_007", "P60_847_682_008", "P60_847_682_009", "P60_847_682_010",
    ];

    // Story 6.9: every sample is cold, so its Coulee must be on file for the tick to time a real insert
    // rather than a cold-Coulee rejection. Mapped once; seeded when each scanner is built, before the
    // stopwatch starts.
    private static readonly Lazy<Kape22ImportBundle[]> SampleBundles =
        new(() => [.. TenFichiers.Select(name => MapFichier(ReadValidFixture(name), name))]);

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

    // A fresh scanner over a fresh EF InMemory database, so each run starts with an empty L_D_KAPE22 and
    // the D22 guard never short-circuits a first insert.
    private static InboxScanner Scanner(InMemoryFileSource source)
    {
        InMemoryContextFactory contexts = new();
        foreach (Kape22ImportBundle bundle in SampleBundles.Value)
        {
            SeedCoulee(contexts.Reader, bundle);
        }

        Kape22FichierProcessor processor = new(
            contexts.Next,
            Configuration(),
            contexts.Journal,
            Options(),
            new FixedClock(Now),
            NullLogger<Kape22FichierProcessor>.Instance);

        return new InboxScanner(source, processor, Options(), new FixedClock(Now), new RecordingLogger<InboxScanner>());
    }

    // NFR-1: a single Fichier through the whole pipeline averages well under the 200 ms end-to-end
    // budget once FTP and SQL latency are excluded. Measured over repeated ticks after a warm-up tick,
    // each on its own scanner and database; Story 6.9: every scanner (and its seeded Coulees) is built
    // before the stopwatch starts, so only the ticks are timed.
    [Fact]
    [Trait("NFR", "1")]
    public void Tick_SingleFichier_StaysUnderTheEndToEndBudget_Nfr1()
    {
        byte[] content = ReadValidFixture(ReferenceFichierName);
        const double budgetMs = 200;
        const int iterations = 30;

        Scanner(SingleFichierSource(content)).RunTick();
        InMemoryFileSource[] sources = [.. Enumerable.Range(0, iterations).Select(_ => SingleFichierSource(content))];
        InboxScanner[] scanners = [.. sources.Select(Scanner)];

        Stopwatch stopwatch = Stopwatch.StartNew();
        foreach (InboxScanner scanner in scanners)
        {
            scanner.RunTick();
        }

        stopwatch.Stop();

        // Story 6.9: every tick imported the Fichier - none was rejected for a missing cold Coulee.
        Assert.All(sources, source => Assert.Empty(source.ListRecursive("error")));
        double averageMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        Assert.True(
            averageMs < budgetMs,
            $"One Fichier end to end averaged {averageMs:F1} ms, above the {budgetMs} ms NFR-1 budget (FTP/SQL excluded).");
    }

    // NFR-2: one tick of 500 Fichiers finishes far under 30 s. The 500 are the ten samples cycled 50
    // times under distinct names. Story 6.9: the scanner (and its seeded Coulees) is built before the
    // stopwatch starts, so only the tick is timed.
    // ponytail: the 50 copies of each sample share a Header roulette, so the D22 guard skips the insert
    // on the repeats - the tick still runs Kape22FichierProcessor's full pipeline (Kape22ImportBundleMapper,
    // then Kape22Persister's guard query) per Fichier. Swap in 500 distinct Headers if NFR-2 must time 500
    // real inserts.
    [Fact]
    [Trait("NFR", "2")]
    public void Tick_FiveHundredFichiers_FinishesWellUnderThirtySeconds_Nfr2()
    {
        byte[][] pool = [.. TenFichiers.Select(ReadValidFixture)];
        InMemoryFileSource source = new();
        for (int i = 0; i < 500; i++)
        {
            source.Add(InboxRoot, $"P60_847_682_{i:D5}", pool[i % pool.Length], Now.AddMinutes(-1).AddSeconds(-i));
        }

        InboxScanner scanner = Scanner(source);

        Stopwatch stopwatch = Stopwatch.StartNew();
        scanner.RunTick();
        stopwatch.Stop();

        Assert.Empty(source.Names(InboxRoot));

        // Story 6.9: no Fichier was rejected for a missing cold Coulee.
        Assert.Empty(source.ListRecursive("error"));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"A 500-Fichier tick took {stopwatch.Elapsed.TotalSeconds:F1} s, above the 30 s NFR-2 budget (FTP/SQL excluded).");
    }

    // An inbox holding only the given Fichier.
    private static InMemoryFileSource SingleFichierSource(byte[] content)
    {
        InMemoryFileSource source = new();
        source.Add(InboxRoot, ReferenceFichierName, content, Now.AddMinutes(-1));
        return source;
    }
}
