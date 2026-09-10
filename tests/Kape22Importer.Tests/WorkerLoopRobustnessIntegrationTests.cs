using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.5 / FR-15: the parts of the worker loop robustness that need real I/O (AR-12), so they are
// Integration category and excluded from the Category=Unit CI run. The recycle half (AC-FR15-4, NFR-7):
// a worker restarted in the middle of a batch resumes completely on the next tick, with no lost Fichier
// and no duplicate insert - the anti-duplicate guard of Story 2.8 (D22) is what stops a second
// L_D_KAPE22 row when a Fichier was committed just before the crash; these SkippableFact tests need a
// reachable local SQL Server test instance and skip cleanly otherwise, and run in the commit + reset
// regime (ResetData first). The persistence-failure half (AC-FR15-3): the real Kape22FichierProcessor
// against a context pointed at a dead host/port always runs (no SQL Server needed) and proves the real
// SqlException -> PersistenceError -> processing/ wiring. The file lifecycle runs over the in-memory
// IFileSource. Written test-first (CC-1). Vocabulary follows the PRD glossary (CC-5).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class WorkerLoopRobustnessIntegrationTests(SqlServerIntegrationFixture fixture)
{
    private const string InitiatingServer = "AFS017";

    // A fixed summer instant: Paris is UTC+2, so the archive date folder is 2026/09.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    private const string ArchiveDateFolder = "archive/2026/09";

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

    // A fresh InboxScanner over a real Kape22FichierProcessor - one worker lifetime. Building a new one
    // models the worker being recycled between ticks.
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

    // AC-FR15-4: a batch interrupted mid-way - one Fichier stranded in processing/ by the crash, another
    // still pending in the inbox - is fully resumed by the next tick of the recycled worker. Both
    // Fichiers are inserted exactly once and archived; nothing is left in processing/ or the inbox.
    [SkippableFact]
    [Trait("AC", "FR15-4")]
    public void Tick_RecycledWorker_ResumesTheStrandedAndThePendingFichier_WithoutLoss_AcFr15_4()
    {
        Ready();
        InMemoryFileSource source = new();
        source.Add("processing", "P60_847_682_001", ReadValidFixture("P60_847_682_001"), Now.AddMinutes(-5));
        source.Add("", "P60_847_682_002", ReadValidFixture("P60_847_682_002"), Now.AddMinutes(-1));

        Scanner(source).RunTick();

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Equal(2, verify.Kape22Rows.AsNoTracking().Count());
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_001"));
        Assert.True(source.Exists(ArchiveDateFolder, "P60_847_682_002"));
        Assert.Empty(source.Names("processing"));
        Assert.Empty(source.Names(string.Empty));
    }

    // AC-FR15-4 (D22): the worker crashes after the L_D_KAPE22 commit but before the archive move, so
    // the Fichier is stranded in processing/ with its row already in the database. The recycled worker
    // reprocesses it, the anti-duplicate guard recognises the prior success, and no second row is
    // inserted - the Fichier is archived, not sent to error/.
    [SkippableFact]
    [Trait("AC", "FR15-4")]
    public void Tick_RecycleAfterCommitBeforeMove_DoesNotInsertTwice_AcFr15_4()
    {
        Ready();
        InMemoryFileSource source = new();
        source.Add("", ReferenceFichierName, ReadValidFixture(ReferenceFichierName), Now.AddMinutes(-3));

        Scanner(source).RunTick();
        Assert.True(source.Exists(ArchiveDateFolder, ReferenceFichierName), "first tick must archive the Fichier.");

        // The crash: the worker died after the commit but before Archive ran, so neither the Fichier nor
        // its .xml sidecar ever reached archive/ and the Fichier is still in processing/.
        source.Add("processing", ReferenceFichierName, ReadValidFixture(ReferenceFichierName), Now.AddMinutes(-2));
        source.Delete(ArchiveDateFolder, ReferenceFichierName);
        source.Delete(ArchiveDateFolder, ReferenceFichierName + ".xml");

        Scanner(source).RunTick();

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Single(verify.Kape22Rows.AsNoTracking());
        Assert.True(source.Exists(ArchiveDateFolder, ReferenceFichierName), "the resumed Fichier must be archived, not quarantined.");
        Assert.True(source.Exists(ArchiveDateFolder, ReferenceFichierName + ".xml"), "the resume must rewrite the .xml sidecar.");
        Assert.False(source.Exists("error", ReferenceFichierName));
        Assert.Empty(source.Names("processing"));
    }

    // AC-FR15-3: the real Kape22FichierProcessor with a context factory pointed at a dead host/port -
    // Converter and Kape22Mapper succeed, Kape22Persister catches the SqlException and returns a
    // PersistenceError - and InboxScanner leaves the Fichier in processing/, never in error/. Proves the
    // whole persister -> processor -> scanner wiring routes an unreachable database to a retry. Real TCP
    // I/O (a loopback connect that fails fast), so Integration, but no SQL Server instance is needed.
    [Fact]
    [Trait("AC", "FR15-3")]
    public void Tick_RealPipelineWithAnUnreachableDatabase_LeavesFichierInProcessing_AcFr15_3()
    {
        InMemoryFileSource source = new();
        source.Add("", ReferenceFichierName, ReadValidFixture(ReferenceFichierName), Now.AddMinutes(-1));
        Kape22FichierProcessor processor = new(
            DeadDatabaseContext,
            Configuration(),
            Options(),
            new FixedClock(Now),
            NullLogger<Kape22FichierProcessor>.Instance);
        RecordingLogger<InboxScanner> logger = new();

        new InboxScanner(source, processor, Options(), new FixedClock(Now), logger).RunTick();

        Assert.True(source.Exists("processing", ReferenceFichierName), "an unreachable database must leave the Fichier in processing/.");
        Assert.False(source.Exists("error", ReferenceFichierName));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    // A context bound to loopback port 1, where nothing ever listens: the first EF query fails fast with
    // a SqlException (a DbException), which Kape22Persister translates to a PersistenceError. Port 1 is
    // privileged and outside the ephemeral range, so it cannot be transiently bound on a CI host. No
    // SQL Server needed.
    private static AscoLsiDbContext DeadDatabaseContext() =>
        new(new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=Dead;Connect Timeout=1;Encrypt=False;ConnectRetryCount=0")
            .Options);
}
