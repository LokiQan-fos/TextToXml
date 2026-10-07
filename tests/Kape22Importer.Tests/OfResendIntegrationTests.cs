using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AscoLsiJournal;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 6.10 (D34): a Fichier whose OF already sits in L_D_ORDRE_FABRICATION. The OF is refused - one
// BusinessRuleViolation, no write, one REJETÉ entry - when its Etat is ENC/EVC/ENFOURNE/LAMINAGE/LAMINE or
// when it appears in L_D_FOURS.OFEnCours, L_D_PLANS_FOURS or L_D_PSO (AC-FR20-6). Otherwise its rows are
// deleted the way the legacy DeleteOF does (13 tables, L_D_OF_SUIVI re-ranked) and the new ones inserted,
// in one explicit transaction around the single SaveChanges, L_D_COULEE and the earlier L_D_KAPE22 rows
// untouched, and a SQL failure leaves the previous OF intact (AC-FR21-6, AC-FR21-2). The real cases come from P60/ and P60/error/ (443..449); the per-table cases re-send the
// reference Fichier under another NumeroFichier. Integration category (AR-12), commit + reset regime.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class OfResendIntegrationTests(SqlServerIntegrationFixture fixture)
{
    // REFUSE_STATE: 443 and 444 created OF 2040310 / 2040311, both then EVC; 446 re-sends 2040310, 447
    // and 449 re-send 2040311 - each is refused for the state, nothing changes.
    [SkippableFact]
    [Trait("AC", "FR20-6")]
    public void Import_ExistingOfInAProtectedState_IsRefusedWithoutAnyWrite_AcFr20_6()
    {
        Ready(seedReferenceCoulee: false);
        SeedCouleeOf("P60_847_682_443");
        SeedCouleeOf("P60_847_682_444");
        AssertAccepted(Import("P60_847_682_443"));
        AssertAccepted(Import("P60_847_682_444"));
        SetEtat(2, "2040310", "2040311");

        AssertRefused(() => Import("error/P60_847_682_446"), "2040310", "état EVC (2)");
        AssertRefused(() => Import("error/P60_847_682_447"), "2040311", "état EVC (2)");
        AssertRefused(() => Import("error/P60_847_682_449"), "2040311", "état EVC (2)");
    }

    // REFUSE_STATE, every protected legacy EtatOF: the reference OF, imported then moved to that state,
    // re-sent under another NumeroFichier, is refused naming the state, nothing changes.
    [SkippableTheory]
    [InlineData(1, "ENC")]
    [InlineData(2, "EVC")]
    [InlineData(3, "ENFOURNE")]
    [InlineData(5, "LAMINAGE")]
    [InlineData(8, "LAMINE")]
    [Trait("AC", "FR20-6")]
    public void Persist_ExistingOfInEachProtectedState_IsRefusedNamingTheState_AcFr20_6(int etat, string name)
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        SetEtat(etat, reference.OF!);

        AssertRefused(() => Persist(fixture, Resent()), reference.OF!, $"état {name} ({etat})");
    }

    // A non-protected state other than GPAO (4, ANNULEE): the existing OF is replaced, not refused.
    [SkippableFact]
    [Trait("AC", "FR21-6")]
    public void Persist_ExistingOfInStateAnnulee_IsReplaced_AcFr21_6()
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        SetEtat(4, reference.OF!);

        ImportResult result = Persist(fixture, Resent());

        AssertAccepted(result);
        Assert.False(result.AlreadyImported);
        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        L_D_ORDRE_FABRICATION replaced = Assert.Single(verify.OrdreFabricationRows.AsNoTracking());
        Assert.Equal("999", replaced.NumeroFichier.Trim());
        Assert.Equal(0, replaced.Etat);
        Assert.Equal(2, verify.Kape22Rows.Count());
    }

    // REFUSE_TABLE: the reference OF, imported in state GPAO (0), then placed in one precondition table;
    // re-sent under another NumeroFichier it is refused for that table.
    [SkippableTheory]
    [InlineData("L_D_FOURS", "INSERT INTO dbo.L_D_FOURS ([Id], [OFEnCours]) VALUES (N'21', {0});")]
    [InlineData("L_D_PLANS_FOURS", "INSERT INTO dbo.L_D_PLANS_FOURS ([FourId], [Position], [OF]) VALUES (N'21', 1, {0});")]
    [InlineData("L_D_PSO", "INSERT INTO dbo.L_D_PSO ([Coulee], [NumeroLingot], [OF]) VALUES (N'065718', 1, {0});")]
    [Trait("AC", "FR20-6")]
    public void Persist_ExistingOfInAPreconditionTable_IsRefusedNamingTheTable_AcFr20_6(string table, string insert)
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        using (AscoLsiDbContext context = fixture.NewAscoLsiContext())
        {
            context.Database.ExecuteSqlRaw(insert, DownstreamOf.Pad(reference.OF!));
        }

        AssertRefused(() => Persist(fixture, Resent()), reference.OF!, $"présent dans {table}");
    }

    // Review P-2: every reason is named, the state first, then each precondition table in turn, joined by
    // " ; ".
    [SkippableFact]
    [Trait("AC", "FR20-6")]
    public void Persist_ExistingOfWithSeveralRefusalReasons_NamesThemAllInOrder_AcFr20_6()
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        SetEtat(2, reference.OF!);
        using (AscoLsiDbContext context = fixture.NewAscoLsiContext())
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO dbo.L_D_FOURS ([Id], [OFEnCours]) VALUES (N'21', {0}); " +
                "INSERT INTO dbo.L_D_PSO ([Coulee], [NumeroLingot], [OF]) VALUES (N'065718', 1, {0});",
                DownstreamOf.Pad(reference.OF!));
        }

        AssertRefused(
            () => Persist(fixture, Resent()),
            reference.OF!,
            "état EVC (2) ; présent dans L_D_FOURS ; présent dans L_D_PSO");
    }

    // Review P-9 (D-1, legacy DeleteOF): the replace also deletes the OF's L_D_OF_SUIVI row, moving every
    // later Rang up by one, and its L_D_MAM_QUAL, L_D_PRODUITS_OUTIL and L_D_REBUT rows; other OF keep
    // theirs.
    [SkippableFact]
    [Trait("AC", "FR21-6")]
    public void Persist_ExistingReplaceableOf_DeletesItsRowsInTheOtherLegacyDeleteOfTables_AcFr21_6()
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        SeedLegacyDeleteOfRows(reference.OF!);

        AssertAccepted(Persist(fixture, Resent()));

        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Equal(
            ["000000000001:1", "000000000003:2"],
            verify.OfSuiviRows.AsNoTracking().OrderBy(row => row.Rang).Select(row => row.OF + ":" + row.Rang).ToList());
        Assert.Equal(["000000000003"], verify.MamQualRows.AsNoTracking().Select(row => row.OF).ToList());
        Assert.Equal(["000000000003"], verify.ProduitsOutilRows.AsNoTracking().Select(row => row.OF).ToList());
        Assert.Equal(["000000000003"], verify.RebutRows.AsNoTracking().Select(row => row.OF).ToList());
    }

    // Review P-1: re-sent with fewer rows (2 sections and half the Consignes gone), the OF keeps none of its
    // previous rows - the 9 tables hold exactly what the reduced Fichier produces on its own.
    [SkippableFact]
    [Trait("AC", "FR21-6")]
    public void Persist_ResentOfWithFewerRows_LeavesNoStaleRow_AcFr21_6()
    {
        Ready();
        AssertAccepted(Persist(fixture, Reduced()));
        List<string> expected = DownstreamSnapshot();

        Ready();
        AssertAccepted(Persist(fixture, MapReferenceBundle()));
        Assert.NotEqual(expected, DownstreamSnapshot());

        AssertAccepted(Persist(fixture, Reduced()));

        Assert.Equal(expected, DownstreamSnapshot());
    }

    // REPLACE: 445 created OF 2040312 (state GPAO, in no precondition table); 448 re-sends it and is
    // accepted. The 9 downstream tables then hold exactly what 448 alone produces, 445's L_D_KAPE22 row
    // and the Coulee are unchanged, and 448 adds its own L_D_KAPE22 row.
    [SkippableFact]
    [Trait("AC", "FR21-6")]
    public void Import_ExistingReplaceableOf_ReplacesItsDownstreamRows_AcFr21_6()
    {
        Ready(seedReferenceCoulee: false);
        SeedCouleeOf("P60_847_682_448");
        AssertAccepted(Import("P60_847_682_448"));
        List<string> expected = DownstreamSnapshot();

        Ready(seedReferenceCoulee: false);
        SeedCouleeOf("P60_847_682_445");
        AssertAccepted(Import("P60_847_682_445"));
        List<string> coulee = Rows(fixture.NewAscoLsiContext, context => context.CouleeRows);
        List<string> kape22 = Rows(fixture.NewAscoLsiContext, context => context.Kape22Rows);

        ImportResult result = Import("P60_847_682_448");

        AssertAccepted(result);
        Assert.False(result.AlreadyImported);
        Assert.Equal(expected, DownstreamSnapshot());
        Assert.Equal(coulee, Rows(fixture.NewAscoLsiContext, context => context.CouleeRows));
        List<string> kape22After = Rows(fixture.NewAscoLsiContext, context => context.Kape22Rows);
        Assert.Equal(kape22.Count + 1, kape22After.Count);
        Assert.All(kape22, row => Assert.Contains(row, kape22After));
        using AscoLsiDbContext verify = fixture.NewAscoLsiContext();
        Assert.Equal(["445", "448"], verify.Kape22Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => row.NumeroFichier.Trim()).ToList());
        Assert.EndsWith("— OK", fixture.LogRows()[^1].Message, StringComparison.Ordinal);
    }

    // REPLACE_SQL_FAIL: the replace fails on SQL Server (over-long LibelleConsigneChutage, an L_D_KAPE22
    // column no pre-check bounds) - PersistenceError, and the previous OF stays intact in every table,
    // the 4 other tables the legacy DeleteOF clears too, L_D_OF_SUIVI's Rang included (review P-9).
    [SkippableFact]
    [Trait("AC", "FR21-2")]
    [Trait("AC", "FR21-6")]
    public void Persist_ReplaceThatFailsOnSqlServer_LeavesThePreviousOfIntact_AcFr21_6()
    {
        Ready();
        Kape22ImportBundle reference = MapReferenceBundle();
        AssertAccepted(Persist(fixture, reference));
        SeedLegacyDeleteOfRows(reference.OF!);
        List<string> before = Snapshot();
        int logRowsBefore = fixture.LogRows().Count;

        ImportResult result = Persist(fixture, Resent(d => SetChamp(d, "message", "LibelleConsigneChutage", new string('A', 50))));

        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.PersistenceError, error.Code);
        Assert.Equal(before, Snapshot());
        List<L_D_LOG_COMMANDE> logRows = fixture.LogRows();
        Assert.Equal(logRowsBefore + 1, logRows.Count);
        Assert.Contains("REJETÉ", logRows[^1].Message, StringComparison.Ordinal);
    }

    private static byte[] Read(string relativePath) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"P60/{relativePath}"));

    // A fixed instant after every Fichier of this class was received (AR-12).
    private static TimeProvider Clock() =>
        new FixedClock(DateTimeOffset.Parse("2026-10-01T12:00:00Z", CultureInfo.InvariantCulture));

    // The reference Fichier re-sent under another NumeroFichier (so the D22 guard does not skip it), its
    // cold Coulee kept at the seeded "065718", with an optional extra mutation.
    private static Kape22ImportBundle Resent(Action<XDocument>? mutate = null) =>
        MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "header", "NumeroFichier", "999");
            mutate?.Invoke(d);
        });

    // The reference Fichier re-sent with 2 of its sections and half of its Consignes gone.
    private static Kape22ImportBundle Reduced()
    {
        Kape22ImportBundle resent = Resent();
        Assert.NotNull(resent.SectionChargeChutage);
        Assert.NotNull(resent.SectionChargeRefroidissoirs);
        return resent with
        {
            Consignes = [.. resent.Consignes.Take(resent.Consignes.Count / 2)],
            SectionChargeChutage = null,
            SectionChargeRefroidissoirs = null,
        };
    }

    // Every row of L_D_KAPE22, L_D_COULEE, the 9 downstream tables and the 4 other tables the D34 replace
    // deletes from, one line per row.
    private List<string> Snapshot() =>
    [
        .. Rows(fixture.NewAscoLsiContext, context => context.CouleeRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.Kape22Rows),
        .. DownstreamSnapshot(),
        .. Rows(fixture.NewAscoLsiContext, context => context.MamQualRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.OfSuiviRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.ProduitsOutilRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.RebutRows),
    ];

    // Every row of the 9 downstream tables D34 replaces, one line per row.
    private List<string> DownstreamSnapshot() =>
    [
        .. Rows(fixture.NewAscoLsiContext, context => context.ConsignesRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.OrdreFabricationRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargeChutageRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargeDecoupeRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargeLingotRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargePitsRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargePoidsMetriqueRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargeRefroidissoirsRows),
        .. Rows(fixture.NewAscoLsiContext, context => context.SectionChargeSvtRows),
    ];

    // The table's rows, each one "<Entity>: <every property value>", in ordinal order.
    private static List<string> Rows<TEntity>(Func<AscoLsiDbContext> newContext, Func<AscoLsiDbContext, DbSet<TEntity>> table)
        where TEntity : class
    {
        using AscoLsiDbContext context = newContext();
        return
        [
            .. table(context).AsNoTracking().AsEnumerable()
                .Select(row => $"{typeof(TEntity).Name}: " + string.Join(" | ", typeof(TEntity).GetProperties()
                    .Select(property => Convert.ToString(property.GetValue(row), CultureInfo.InvariantCulture))))
                .Order(StringComparer.Ordinal),
        ];
    }

    // The refusal shape shared with the cold-Coulee check: one File-level BusinessRuleViolation naming the
    // OF and the reason, no row changed anywhere, one new REJETÉ entry naming the OF's reason.
    private void AssertRefused(Func<ImportResult> act, string of, string reason)
    {
        List<string> before = Snapshot();
        int logRowsBefore = fixture.LogRows().Count;

        ImportResult result = act();

        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Equal($"OF '{of}' : l'OF existe déjà et ne peut pas être remplacé ({reason}).", error.Message);
        Assert.Equal(before, Snapshot());
        List<L_D_LOG_COMMANDE> logRows = fixture.LogRows();
        Assert.Equal(logRowsBefore + 1, logRows.Count);
        Assert.Contains("REJETÉ", logRows[^1].Message, StringComparison.Ordinal);
        Assert.Contains(reason, logRows[^1].Message, StringComparison.Ordinal);
    }

    private ImportResult Import(string relativePath) =>
        new Kape22FichierProcessor(
                fixture.NewAscoLsiContext,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Import:Commande"] = "P60",
                    ["Import:InitiatingServer"] = SqlServerIntegrationFixture.JournalInitiatingServer,
                }).Build(),
                fixture.NewJournal(),
                new ImportOptions
                {
                    ArchiveFolder = "archive",
                    ErrorFolder = "error",
                    InboxPath = "inbox",
                    InitiatingServer = SqlServerIntegrationFixture.JournalInitiatingServer,
                    PollingInterval = TimeSpan.FromSeconds(30),
                    ProcessingFolder = "processing",
                    RetentionDays = 30,
                },
                Clock(),
                NullLogger<Kape22FichierProcessor>.Instance)
            .Import(Path.GetFileName(relativePath), Read(relativePath));

    // The reference Fichier is cold, so its Coulee ("065718") is on file unless the test seeds its own.
    private void Ready(bool seedReferenceCoulee = true)
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();
        if (seedReferenceCoulee)
        {
            SeedCoulees(fixture.NewAscoLsiContext, ReferenceFichierName);
        }
    }

    // Puts the Fichier's own Coulee in L_D_COULEE, the state a cold Coulee is in once AscoLSI tracks it.
    private void SeedCouleeOf(string relativePath) =>
        SeedCoulee(fixture.NewAscoLsiContext, MapFichier(Read(relativePath), Path.GetFileName(relativePath)));

    // The OF in the middle of the L_D_OF_SUIVI queue (between 2 other OF), with one L_D_MAM_QUAL row, 2
    // L_D_PRODUITS_OUTIL rows and 2 L_D_REBUT rows; another OF has one row in each of these 3 tables.
    private void SeedLegacyDeleteOfRows(string of)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.Database.ExecuteSqlRaw(
            "INSERT INTO dbo.L_D_OF_SUIVI ([OF], [Rang]) VALUES (N'000000000001', 1), ({0}, 2), (N'000000000003', 3); " +
            "INSERT INTO dbo.L_D_MAM_QUAL ([OF]) VALUES ({0}), (N'000000000003'); " +
            "INSERT INTO dbo.L_D_PRODUITS_OUTIL ([Zone], [OF]) VALUES (N'blooming', {0}), (N'scarfing', {0}), (N'blooming', N'000000000003'); " +
            "INSERT INTO dbo.L_D_REBUT ([OF]) VALUES ({0}), ({0}), (N'000000000003');",
            DownstreamOf.Pad(of));
    }

    // Moves the named OF to the given Etat, the way the legacy screens do once production starts.
    private void SetEtat(int etat, params string[] ofs)
    {
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        foreach (string of in ofs)
        {
            context.OrdreFabricationRows.Single(row => row.OF == DownstreamOf.Pad(of)).Etat = etat;
        }

        context.SaveChanges();
    }
}
