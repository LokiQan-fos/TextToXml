using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FichierJournal;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 6.1 (FR-24, D31, D22 revised): Kape22Persister no longer writes L_D_LOG_COMMANDE itself - it
// records every outcome with a readable OF through IFichierJournal, after the business transaction, and
// its D22 guard reads L_D_KAPE22. These Category=Unit tests drive the persister over an EF in-memory
// AscoLsiDbContext with a recording journal, one test per row of the spec's I/O matrix, plus the
// AC-FR24-1 boundary of the importer project. The real L_D_LOG_COMMANDE rows are asserted on SQL Server
// in TransactionalPersistenceTests and JournalMessageParityIntegrationTests (AR-12). Written test-first
// (CC-1).
[Trait("Category", TestCategory.Unit)]
public class FichierJournalMigrationTests
{
    // AC-FR24-1: the importer carries no L_D_LOG_COMMANDE entity, column lengths or row rule, its model
    // maps no L_D_LOG_COMMANDE table, and it references the journal contract, never the LSI implementation.
    [Fact]
    [Trait("AC", "FR24-1")]
    public void Importer_KeepsNoCopyOfTheLsiJournal_AcFr24_1()
    {
        string[] typeNames = [.. typeof(Kape22Persister).Assembly.GetTypes().Select(type => type.Name)];
        Assert.DoesNotContain("L_D_LOG_COMMANDE", typeNames);
        Assert.DoesNotContain("LogCommandeColumnLengths", typeNames);

        using AscoLsiDbContext context = new(
            new DbContextOptionsBuilder<AscoLsiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        Assert.DoesNotContain(context.Model.GetEntityTypes(), entity => entity.GetTableName() == "L_D_LOG_COMMANDE");

        string persister = File.ReadAllText(RepoLayout.ProjectFile("src/Kape22Importer/Persistence/Kape22Persister.cs"));
        Assert.DoesNotContain("ParisTime", persister, StringComparison.Ordinal);
        Assert.DoesNotContain("— OK", persister, StringComparison.Ordinal);
        Assert.DoesNotContain("— REJETÉ", persister, StringComparison.Ordinal);

        string project = File.ReadAllText(RepoLayout.ProjectFile("src/Kape22Importer/Kape22Importer.csproj"));
        Assert.Contains(@"..\FichierJournal\FichierJournal.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("AscoLsiJournal", project, StringComparison.Ordinal);
    }

    // AC-FR24-2: success -> the rows are committed first, then one success entry (no reason) carries the
    // configured Commande, the Fichier name, the clock instant, NumeroFichier and OF.
    [Fact]
    [Trait("AC", "FR24-2")]
    public void Persist_Success_RecordsTheSuccessEntryAfterTheCommit_AcFr24_2()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapReferenceBundle();
        int committedAtRecord = -1;
        RecordingJournal journal = new(onRecord: () =>
        {
            using AscoLsiDbContext probe = contexts.Reader();
            committedAtRecord = probe.Kape22Rows.Count();
        });

        ImportResult result = Persist(contexts, journal, bundle);

        Assert.NotNull(result.InsertedId);
        Assert.Equal(1, committedAtRecord);
        FichierJournalEntry entry = Assert.Single(journal.Recorded);
        Assert.Equal("P60", entry.Commande);
        Assert.Equal(ReferenceFichierName, entry.FichierName);
        Assert.Equal(WinterClock().GetUtcNow(), entry.Instant);
        Assert.Equal(bundle.NumeroFichier, entry.NumeroFichier);
        Assert.Equal(bundle.OF, entry.OF);
        Assert.Empty(entry.Reasons);
    }

    // AC-FR24-3: a business rejection -> no business row, one failure entry whose single reason is the
    // unchanged P60 message tail.
    [Fact]
    [Trait("AC", "FR24-3")]
    public void Persist_Rejection_RecordsOneFailureEntryWithTheSummary_AcFr24_3()
    {
        InMemoryContextFactory contexts = new();
        RecordingJournal journal = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d => SetChamp(d, "message", "Client", string.Empty));

        ImportResult result = Persist(contexts, journal, bundle);

        Assert.False(result.Success);
        FichierJournalEntry entry = Assert.Single(journal.Recorded);
        string reason = Assert.Single(entry.Reasons);
        Assert.StartsWith("1 erreur(s) : ", reason, StringComparison.Ordinal);
        Assert.Equal(bundle.OF!.Trim(), entry.OF);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
    }

    // AC-FR24-3: an SQL failure -> the failed SaveChanges rolls back, and one failure entry carries the
    // PersistenceError with the server's own message.
    [Fact]
    [Trait("AC", "FR24-3")]
    public void Persist_SqlFailure_RecordsAFailureEntryCarryingTheSqlCause_AcFr24_3()
    {
        RecordingJournal journal = new();
        using FailingSaveContext context = new();

        ImportResult result = new Kape22Persister(context, Configuration(), journal, ReferenceFichierName, WinterClock())
            .Persist(MapReferenceBundle());

        Assert.Equal(ErrorCode.PersistenceError, Assert.Single(result.Errors).Code);
        string reason = Assert.Single(Assert.Single(journal.Recorded).Reasons);
        Assert.Equal(
            "1 erreur(s) : Échec de la persistance dans AscoLSI : Violation of PRIMARY KEY constraint in table 'dbo.L_D_ORDRE_FABRICATION'.",
            reason);
    }

    // AC-FR24-3 / AC-FR24-5: a rejection whose journal write fails keeps its reasons and adds a File-level
    // PersistenceError, so the Fichier stays in processing/ for a retry instead of reaching error/
    // without its journal entry.
    [Fact]
    [Trait("AC", "FR24-3")]
    public void Persist_RejectionWhenTheJournalFails_AddsAPersistenceError_AcFr24_3()
    {
        InMemoryContextFactory contexts = new();
        FailingJournal journal = new(new InvalidOperationException("journal down"));
        Kape22ImportBundle bundle = MapMutatedBundle(d => SetChamp(d, "message", "Client", string.Empty));

        ImportResult result = Persist(contexts, journal, bundle);

        Assert.Equal(bundle.Errors.Count + 1, result.Errors.Count);
        ConversionError last = result.Errors[^1];
        Assert.Equal(Block.File, last.Block);
        Assert.Equal(ErrorCode.PersistenceError, last.Code);
        Assert.Contains("journal down", last.Message, StringComparison.Ordinal);
    }

    // AC-FR24-4: guard hit and the journal already holds the success entry -> nothing inserted, nothing
    // recorded.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void Persist_GuardHitWithJournaledSuccess_RecordsNothing_AcFr24_4()
    {
        InMemoryContextFactory contexts = new();
        SeedKape22Row(contexts);
        RecordingJournal journal = new(hasSuccess: true);

        ImportResult result = Persist(contexts, journal, MapReferenceBundle());

        Assert.True(result.AlreadyImported);
        Assert.Empty(result.Errors);
        Assert.Empty(journal.Recorded);
    }

    // AC-FR24-4: guard hit without a journaled success -> nothing inserted, the success entry recorded.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void Persist_GuardHitWithoutJournaledSuccess_RecordsTheSuccessEntry_AcFr24_4()
    {
        InMemoryContextFactory contexts = new();
        SeedKape22Row(contexts);
        RecordingJournal journal = new(hasSuccess: false);

        ImportResult result = Persist(contexts, journal, MapReferenceBundle());

        Assert.True(result.AlreadyImported);
        Assert.Empty(Assert.Single(journal.Recorded).Reasons);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Single(verify.Kape22Rows);
    }

    // AC-FR24-4 / AC-FR24-5: guard hit while the journal cannot be read -> still no insert, and a
    // PersistenceError keeps the Fichier in processing/ until the journal answers.
    [Fact]
    [Trait("AC", "FR24-4")]
    public void Persist_GuardHitWhenTheJournalCannotBeRead_AddsAPersistenceError_AcFr24_4()
    {
        InMemoryContextFactory contexts = new();
        SeedKape22Row(contexts);
        FailingJournal journal = new(new InvalidOperationException("journal down"), failReads: true);

        ImportResult result = Persist(contexts, journal, MapReferenceBundle());

        Assert.True(result.AlreadyImported);
        Assert.Equal(ErrorCode.PersistenceError, Assert.Single(result.Errors).Code);
        Assert.Empty(journal.Attempted);
    }

    // AC-FR24-5: the journal fails after the commit -> the committed rows and InsertedId stay, and a
    // File-level PersistenceError sends the Fichier back through the retry path.
    [Fact]
    [Trait("AC", "FR24-5")]
    public void Persist_JournalFailsAfterTheCommit_KeepsInsertedIdAndAddsAPersistenceError_AcFr24_5()
    {
        InMemoryContextFactory contexts = new();
        FailingJournal journal = new(new InvalidOperationException("journal down"));

        ImportResult result = Persist(contexts, journal, MapReferenceBundle());

        Assert.NotNull(result.InsertedId);
        Assert.Equal(ErrorCode.PersistenceError, Assert.Single(result.Errors).Code);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Single(verify.Kape22Rows);
    }

    // D15: a rejection with no readable OF records nothing.
    [Fact]
    [Trait("AC", "FR24-3")]
    public void Persist_RejectionWithoutReadableOf_RecordsNothing_AcFr24_3()
    {
        RecordingJournal journal = new();
        Kape22ImportBundle bundle = new() { Errors = [new ConversionError { Block = Block.File, Message = "x" }] };

        ImportResult result = Persist(new InMemoryContextFactory(), journal, bundle);

        Assert.False(result.Success);
        Assert.Empty(journal.Recorded);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Import:Commande"] = "P60" })
            .Build();

    private static ImportResult Persist(InMemoryContextFactory contexts, IFichierJournal journal, Kape22ImportBundle bundle)
    {
        using AscoLsiDbContext context = contexts.Next();
        return new Kape22Persister(context, Configuration(), journal, ReferenceFichierName, WinterClock()).Persist(bundle);
    }

    private static void SeedKape22Row(InMemoryContextFactory contexts)
    {
        using AscoLsiDbContext context = contexts.Next();
        context.Kape22Rows.Add(MapReferenceBundle().Kape22!);
        context.SaveChanges();
    }

    // Keeps every recorded entry; HasSuccess answers the configured value.
    private sealed class RecordingJournal(bool hasSuccess = false, Action? onRecord = null) : IFichierJournal
    {
        public List<FichierJournalEntry> Recorded { get; } = [];

        public bool HasSuccess(FichierJournalEntry entry) => hasSuccess;

        public void Record(FichierJournalEntry entry)
        {
            onRecord?.Invoke();
            this.Recorded.Add(entry);
        }
    }

    // An in-memory context whose SaveChanges fails the way SQL Server rejects a duplicate key.
    private sealed class FailingSaveContext()
        : AscoLsiDbContext(new DbContextOptionsBuilder<AscoLsiDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options)
    {
        public override int SaveChanges() => throw new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new InvalidOperationException("Violation of PRIMARY KEY constraint in table 'dbo.L_D_ORDRE_FABRICATION'."));
    }
}
