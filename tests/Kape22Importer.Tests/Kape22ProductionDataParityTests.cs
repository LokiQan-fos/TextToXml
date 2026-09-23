using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Transactions;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Regression parity against the legacy import: every P60 Fichier in P60/ was already imported into the
// production L_D_KAPE22 by the current application without error. This test runs the same Fichier
// through the new pipeline (Converter -> Kape22Mapper), inserts the mapped entity into the TEST
// database inside a rolled-back transaction, reads the row back, and compares it column by column
// against the row the legacy import wrote in PRODUCTION.
//
// Production is READ-ONLY: this test only ever runs LINQ queries (no Add, no SaveChanges) on the
// production context, over a connection string that should carry ApplicationIntent=ReadOnly and a
// db_datareader-only login. All writes go to the test database and roll back.
//
// Row matching: OF + NumeroFichier (the D22 identity of a Fichier), trailing-space-insensitive; each of
// the 100 sample Fichiers maps to exactly one production row. Columns Id, DateReception and
// DateEnfournementFour1/2 are excluded (identity, worker timestamp, always-NULL D14 slices).
//
// KnownLegacyDivergences catalogues the columns where a CORRECT new insert legitimately differs from
// the legacy production row (see _bmad-output/implementation-artifacts/deferred-work.md, 2026-09-07).
// The test passes as long as every difference falls in that set; a difference on any OTHER column is a
// real regression and fails. When a story teaches Kape22Mapper the legacy default-fill rules, drop the
// corresponding entries here - Client stays permanently (production value is mojibake).
//
// Opt-in: needs BOTH the test instance (ConnectionStrings:AscoLSI, see appsettings.Test.json) AND
// ConnectionStrings:AscoLSI_Production. Absent -> skip. Category=Integration, so CI never runs it.
//
//   dotnet test --filter "FullyQualifiedName~Kape22ProductionDataParityTests"
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class Kape22ProductionDataParityTests(SqlServerIntegrationFixture fixture)
{
    private static readonly Regex FichierName = new(@"^P60_\d+_\d+_\d+$", RegexOptions.Compiled);

    // Ignored on both sides: identity, the worker processing timestamp, and the D14 slices that stay NULL.
    private static readonly HashSet<string> IgnoredColumns = new(StringComparer.Ordinal)
    {
        nameof(L_D_KAPE22.Id),
        nameof(L_D_KAPE22.DateReception),
        nameof(L_D_KAPE22.DateEnfournementFour1),
        nameof(L_D_KAPE22.DateEnfournementFour2),
    };

    // Columns where a correct new insert legitimately differs from the legacy production row. A
    // difference on any column NOT listed here fails the test as a regression. Full write-up in
    // deferred-work.md.
    //   Client : production value is mojibake from a legacy Windows-1252 decoding bug (raw byte 0xD6
    //            = 'Ö'); the new pipeline decodes correctly and intentionally diverges (permanent).
    // OForiginInterne / AcompteSolde / MatriculeClient / ChutagePied were here after the 2026-09-07
    // run; Kape22Mapper's "Legacy blank-Champ defaults" (Annexe B) now reproduces those, so they are
    // expected to match.
    private static readonly HashSet<string> KnownLegacyDivergences = new(StringComparer.Ordinal)
    {
        nameof(L_D_KAPE22.Client),
    };

    private static readonly string ProductionConnectionString =
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables("KAPE22_TEST_")
            .Build()
            .GetConnectionString("AscoLSI_Production") ?? string.Empty;

    private static string P60Directory => RepoLayout.ProjectFile("P60");

    // Every downstream mapper now zero-pads OF to 12 digits itself (DownstreamOf.Pad); this local alias
    // just reads that same production convention at the call sites below.
    private static string PadOf(string of) => DownstreamOf.Pad(of);

    public static TheoryData<string> P60Fichiers()
    {
        TheoryData<string> data = new();
        if (!Directory.Exists(P60Directory))
        {
            return data;
        }

        foreach (string path in Directory.EnumerateFiles(P60Directory)
                     .Where(p => FichierName.IsMatch(Path.GetFileName(p)))
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(P60Fichiers))]
    public void MappedFichier_MatchesLegacyProductionRow(string fichierName)
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        Skip.If(
            string.IsNullOrWhiteSpace(ProductionConnectionString),
            "No production database configured. Set ConnectionStrings:AscoLSI_Production in " +
            "tests/Kape22Importer.Tests/appsettings.Test.json or KAPE22_TEST_ConnectionStrings__AscoLSI_Production.");

        // 1. New pipeline: raw bytes -> normalized XML -> mapped entity.
        byte[] bytes = File.ReadAllBytes(Path.Combine(P60Directory, fichierName));
        ConversionResult conversion = Converter.Convert(bytes, EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, $"Step 1 failed for {fichierName}: {Describe(conversion.Errors)}");

        MapResult<L_D_KAPE22> mapping = new Kape22Mapper(StableClock()).Map(conversion.Xml!, fichierName);
        Assert.True(mapping.Success, $"Step 2 mapping failed for {fichierName}: {Describe(mapping.Errors)}");
        L_D_KAPE22 mapped = mapping.Value!;

        // 2. Round-trip the mapped entity through the test database, then read it back.
        L_D_KAPE22 testRow = RoundTripThroughTestDatabase(mapped);

        // 3. The legacy row this Fichier produced in production. The D22 identity of a Fichier is
        // OF + NumeroFichier (the Header roulette), so an OF re-imported under another roulette is a
        // different row - matching on OF alone would pick the wrong one.
        string of = mapped.OF.Trim();
        string numeroFichier = mapped.NumeroFichier.Trim();
        List<L_D_KAPE22> candidates = ReadProductionRows(of, numeroFichier);

        Skip.If(
            candidates.Count == 0,
            $"{fichierName}: aucune ligne L_D_KAPE22 en production pour OF '{of}' + NumeroFichier '{numeroFichier}' " +
            "(ce Fichier n'a jamais ete importe tel quel).");
        Assert.True(
            candidates.Count == 1,
            $"{fichierName}: {candidates.Count} lignes de production pour OF '{of}' + NumeroFichier '{numeroFichier}' " +
            $"(Id {string.Join(", ", candidates.Select(r => r.Id))}) - impossible de designer la ligne de reference.");

        L_D_KAPE22 productionRow = candidates[0];

        // 4. Compare every non-ignored column. A difference on a KnownLegacyDivergences column is
        // tolerated; a difference anywhere else fails as a regression.
        List<string> regressions = [];
        foreach (PropertyInfo column in typeof(L_D_KAPE22).GetProperties())
        {
            if (IgnoredColumns.Contains(column.Name))
            {
                continue;
            }

            object? expected = Normalize(column.GetValue(productionRow));
            object? actual = Normalize(column.GetValue(testRow));
            if (Equals(expected, actual) || KnownLegacyDivergences.Contains(column.Name))
            {
                continue;
            }

            regressions.Add($"  {column.Name}: production={Format(expected)}, nouveau traitement={Format(actual)}");
        }

        Assert.True(
            regressions.Count == 0,
            $"{fichierName} (OF {of} + NumeroFichier {numeroFichier}) diverge de la production hors ecarts connus :\n" +
            string.Join("\n", regressions));
    }

    // Inserts the entity into the test database under a TransactionScope that is never completed, so the
    // row rolls back on dispose, then returns the value SQL Server actually stored. Mirrors the
    // rollback pattern in PersistenceSmokeTests.
    private L_D_KAPE22 RoundTripThroughTestDatabase(L_D_KAPE22 entity)
    {
        using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.Database.OpenConnection();

        context.Kape22Rows.Add(entity);
        context.SaveChanges();
        int id = entity.Id;

        L_D_KAPE22 reloaded = context.Kape22Rows.AsNoTracking().Single(row => row.Id == id);

        // No scope.Complete(): the insert rolls back here. reloaded is already materialized.
        return reloaded;
    }

    // READ-ONLY production access: a LINQ query only, never Add / SaveChanges.
    private static List<L_D_KAPE22> ReadProductionRows(string of, string numeroFichier)
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer(ProductionConnectionString)
            .Options;

        using AscoLsiDbContext production = new(options);

        // SQL Server '=' on NCHAR is trailing-space-insensitive, so the trimmed keys match the padded
        // columns. NumeroFichier is NVARCHAR(MAX); '=' still ignores trailing spaces.
        return production.Kape22Rows.AsNoTracking()
            .Where(row => row.OF == of && row.NumeroFichier == numeroFichier)
            .OrderBy(row => row.Id)
            .ToList();
    }

    // Story 4.10 (B-3): DecimalScale.Apply's own literal scale (Story 4.3-bis) is only ever checked
    // against the annex (B-1) - never against what the legacy application actually stored for the same
    // Fichier. Round-trips L_D_ORDRE_FABRICATION and the L_D_SECTIONCHARGE_* tables DecimalScale scales
    // (Chutage, Decoupe, Lingot, Pits - Refroidissoirs/PoidsMetrique/Svt carry no decimal column,
    // 4.2-bis) through the test database the same way as MappedFichier_MatchesLegacyProductionRow, then
    // compares only their DownstreamColumnMagnitudes-registered (scaled) columns against the real
    // production row - never a full row: L_D_ORDRE_FABRICATION's non-scaled columns are legitimately
    // rewritten by later, non-P60 GPAO events (annexe-mapping-dispatch-epic4.md), which a full-row
    // compare would flag as a false regression. A table with no production row for this OF, or with the
    // Story 4.4 per-OF applicability rule leaving its mapper output null, has nothing to compare and is
    // silently skipped rather than failing the whole Fichier.
    [SkippableTheory]
    [MemberData(nameof(P60Fichiers))]
    public void MappedFichier_ScaledDownstreamColumns_MatchLegacyProductionRow(string fichierName)
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        Skip.If(
            string.IsNullOrWhiteSpace(ProductionConnectionString),
            "No production database configured. Set ConnectionStrings:AscoLSI_Production in " +
            "tests/Kape22Importer.Tests/appsettings.Test.json or KAPE22_TEST_ConnectionStrings__AscoLSI_Production.");

        byte[] bytes = File.ReadAllBytes(Path.Combine(P60Directory, fichierName));
        ConversionResult conversion = Converter.Convert(bytes, EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, $"Step 1 failed for {fichierName}: {Describe(conversion.Errors)}");

        MapResult<L_D_KAPE22> mapping = new Kape22Mapper(StableClock()).Map(conversion.Xml!, fichierName);
        Assert.True(mapping.Success, $"Step 2 mapping failed for {fichierName}: {Describe(mapping.Errors)}");
        L_D_KAPE22 mapped = mapping.Value!;
        string of = mapped.OF.Trim();

        L_D_ORDRE_FABRICATION? productionOrdre = ReadProductionRow<L_D_ORDRE_FABRICATION>(
            rows => rows.FirstOrDefault(row => row.OF == PadOf(of)));
        Skip.If(
            productionOrdre is null,
            $"{fichierName}: aucune ligne L_D_ORDRE_FABRICATION en production pour OF '{of}' " +
            $"(paddé '{PadOf(of)}').");

        List<string> regressions = [];
        L_D_ORDRE_FABRICATION testOrdre = RoundTrip(
            OrdreFabricationMapper.Map(mapped, StableClock()), rows => rows.Single(row => row.OF == PadOf(of)));
        regressions.AddRange(ScaledRegressions(testOrdre, productionOrdre!));

        CompareSectionCharge(regressions, fichierName, of, SectionChargeChutageMapper.Map(mapped));
        CompareSectionCharge(regressions, fichierName, of, SectionChargeDecoupeMapper.Map(mapped));
        CompareSectionCharge(regressions, fichierName, of, SectionChargeLingotMapper.Map(mapped));
        CompareSectionCharge(regressions, fichierName, of, SectionChargePitsMapper.Map(mapped));

        Assert.True(
            regressions.Count == 0,
            $"{fichierName} (OF {of}) diverge de la production sur une colonne mise à l'échelle :\n" +
            string.Join("\n", regressions));
    }

    // Story 4.4-bis (AC-4): L_D_CONSIGNES carries several rows per OF - its own natural key is
    // (OF, CodeOperation, TypeConsigne, ConsigneGPAO), not OF alone (Story 4.1) - which is exactly the
    // gap that let ConsignesMapper silently under-produce before this story (5 rows vs 60 in production
    // for one real OF, never caught because this table had no parity test at all). Only the
    // ConsigneGPAO=1 rows are in this mapper's own scope (AC-2, sprint-change-proposal-2026-09-23) -
    // ConsigneGPAO=0 belongs to an earlier, out-of-scope process this mapper never produces or checks
    // for. For every row ConsignesMapper actually produces, its exact natural-key match must exist in
    // production with the same CodeConsigne/SizeCodeConsigne; a row with no production match at all is a
    // real regression. LibelleConsigne is excluded from the comparison (out of scope, stays null here -
    // Boundaries & Constraints).
    [SkippableTheory]
    [MemberData(nameof(P60Fichiers))]
    public void MappedFichier_ConsignesRows_MatchLegacyProductionRows(string fichierName)
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        Skip.If(
            string.IsNullOrWhiteSpace(ProductionConnectionString),
            "No production database configured. Set ConnectionStrings:AscoLSI_Production in " +
            "tests/Kape22Importer.Tests/appsettings.Test.json or KAPE22_TEST_ConnectionStrings__AscoLSI_Production.");

        byte[] bytes = File.ReadAllBytes(Path.Combine(P60Directory, fichierName));
        ConversionResult conversion = Converter.Convert(bytes, EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, $"Step 1 failed for {fichierName}: {Describe(conversion.Errors)}");

        MapResult<L_D_KAPE22> mapping = new Kape22Mapper(StableClock()).Map(conversion.Xml!, fichierName);
        Assert.True(mapping.Success, $"Step 2 mapping failed for {fichierName}: {Describe(mapping.Errors)}");
        L_D_KAPE22 mapped = mapping.Value!;
        string of = mapped.OF.Trim();

        List<L_D_CONSIGNES> consignes = ConsignesMapper.Map(
            mapped,
            SectionChargeChutageMapper.Map(mapped),
            SectionChargeDecoupeMapper.Map(mapped),
            SectionChargeLingotMapper.Map(mapped),
            SectionChargePitsMapper.Map(mapped),
            SectionChargePoidsMetriqueMapper.Map(mapped),
            SectionChargeRefroidissoirsMapper.Map(mapped),
            SectionChargeSvtMapper.Map(mapped));

        Skip.If(consignes.Count == 0, $"{fichierName}: aucune section décodable applicable pour cet OF, rien à comparer.");

        List<L_D_CONSIGNES> productionRows = ReadProductionRows<L_D_CONSIGNES>(
            rows => WithOf(rows, PadOf(of)).Where(row => row.ConsigneGPAO).ToList());
        Skip.If(
            productionRows.Count == 0,
            $"{fichierName}: aucune ligne L_D_CONSIGNES (ConsigneGPAO=1) en production pour OF '{of}' (paddé '{PadOf(of)}').");

        List<L_D_CONSIGNES> testRows = RoundTripConsignes(consignes);

        List<string> regressions = [];
        foreach (L_D_CONSIGNES row in testRows)
        {
            // FirstOrDefault, not SingleOrDefault: a data-quality surprise in production (more than one
            // row for this (CodeOperation, TypeConsigne) pair) must still produce the descriptive
            // regression message below, not an opaque InvalidOperationException.
            L_D_CONSIGNES? match = productionRows.FirstOrDefault(
                p => p.CodeOperation.Trim() == row.CodeOperation.Trim() && p.TypeConsigne == row.TypeConsigne);

            if (match is null)
            {
                regressions.Add(
                    $"  CodeOperation={row.CodeOperation}, TypeConsigne={row.TypeConsigne}: aucune ligne de production correspondante.");
                continue;
            }

            if (!Equals(Normalize(match.CodeConsigne), Normalize(row.CodeConsigne)))
            {
                regressions.Add(
                    $"  CodeOperation={row.CodeOperation}, TypeConsigne={row.TypeConsigne}.CodeConsigne: " +
                    $"production={Format(match.CodeConsigne)}, nouveau traitement={Format(row.CodeConsigne)}");
            }

            if (match.SizeCodeConsigne != row.SizeCodeConsigne)
            {
                regressions.Add(
                    $"  CodeOperation={row.CodeOperation}, TypeConsigne={row.TypeConsigne}.SizeCodeConsigne: " +
                    $"production={match.SizeCodeConsigne}, nouveau traitement={row.SizeCodeConsigne}");
            }
        }

        Assert.True(
            regressions.Count == 0,
            $"{fichierName} (OF {of}) diverge de la production sur L_D_CONSIGNES (ConsigneGPAO=1) :\n" +
            string.Join("\n", regressions));
    }

    // Inserts every row ConsignesMapper produced for this OF under one rolled-back TransactionScope, then
    // reads them all back by OF + ConsigneGPAO - the multi-row-per-OF counterpart of RoundTrip (which
    // reloads by a single-row key) and RoundTripThroughTestDatabase (which reloads by Id).
    private List<L_D_CONSIGNES> RoundTripConsignes(List<L_D_CONSIGNES> entities)
    {
        using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.Database.OpenConnection();

        context.ConsignesRows.AddRange(entities);
        context.SaveChanges();

        string of = entities[0].OF;
        List<string> codeOperations = entities.Select(e => e.CodeOperation).Distinct().ToList();

        // No scope.Complete(): the insert rolls back here, same as RoundTrip/RoundTripThroughTestDatabase.
        // Scoped to the CodeOperations just inserted for this call, not just OF: a shared test database
        // carrying other ConsigneGPAO=1 rows for the same OF from an unrelated source would otherwise be
        // silently included.
        return context.ConsignesRows.AsNoTracking()
            .Where(row => row.OF == of && row.ConsigneGPAO && codeOperations.Contains(row.CodeOperation))
            .ToList();
    }

    // A section-charge table is per-OF applicable or not (Story 4.4): a null mapped entity, or no
    // matching production row, means nothing to compare here - not a regression. Unlike
    // L_D_ORDRE_FABRICATION (whose key IS OF, so at most one production row can ever exist), a
    // L_D_SECTIONCHARGE_* table's key is (OF, CodeOperation) - OF alone can legitimately match more than
    // one production row across re-dispatches, so a silent FirstOrDefault could compare against the
    // wrong one; ambiguity fails loudly instead, the same policy ReadProductionRows already applies to
    // L_D_KAPE22 above.
    private void CompareSectionCharge<TEntity>(List<string> regressions, string fichierName, string of, TEntity? mapped)
        where TEntity : class
    {
        if (mapped is null)
        {
            return;
        }

        List<TEntity> candidates = ReadProductionRows<TEntity>(rows => WithOf(rows, PadOf(of)).ToList());
        if (candidates.Count == 0)
        {
            return;
        }

        Assert.True(
            candidates.Count == 1,
            $"{fichierName}: {candidates.Count} lignes de production {typeof(TEntity).Name} pour OF '{of}' " +
            "- impossible de désigner la ligne de référence.");

        // The mapper pads OF before this entity is even constructed (DownstreamOf.Pad), so the same
        // padded key finds it back after the round-trip through the test database.
        TEntity testRow = RoundTrip(mapped, rows => WithOf(rows, PadOf(of)).Single());
        regressions.AddRange(ScaledRegressions(testRow, candidates[0]));
    }

    // Every L_D_SECTIONCHARGE_* entity this test round-trips shares a string OF column (half of its
    // composite key). EF.Property<string> reaches it in a form EF Core actually translates to SQL against
    // a real provider - a PropertyInfo-reflection filter throws "the LINQ expression could not be
    // translated" instead.
    private static IQueryable<TEntity> WithOf<TEntity>(IQueryable<TEntity> rows, string of)
        where TEntity : class =>
        rows.Where(row => EF.Property<string>(row, "OF") == of);

    // L_D_SECTIONCHARGE_CHUTAGE.ChutageTete/ChutagePied are updated by a later, non-P60 GPAO event (the
    // actual post-lamination measurement), confirmed against production 2026-09-22: both real P60
    // re-submissions of the same OF (124, then 128) carry identical raw digits, yet the current
    // production row holds neither - so comparing this test's freshly-dispatched value against
    // production's current row is comparing against a value the P60 dispatch never wrote. Same category
    // as L_D_ORDRE_FABRICATION's non-scaled columns (class doc comment on
    // MappedFichier_ScaledDownstreamColumns_MatchLegacyProductionRow), just not previously known to
    // extend to a scale-guarded column.
    private static readonly HashSet<string> KnownPostDispatchOverwrites = new(StringComparer.Ordinal)
    {
        nameof(L_D_SECTIONCHARGE_CHUTAGE.ChutageTete),
        nameof(L_D_SECTIONCHARGE_CHUTAGE.ChutagePied),
    };

    // Compares only the DownstreamColumnMagnitudes-registered (scale-guarded) decimal columns of two
    // same-typed entities - the columns B-3 exists to check, never the full row.
    private static IEnumerable<string> ScaledRegressions<TEntity>(TEntity testRow, TEntity productionRow)
    {
        foreach (PropertyInfo property in typeof(TEntity).GetProperties())
        {
            if (!DownstreamColumnMagnitudes.MaxAbsoluteValues.ContainsKey(property.Name)
                || KnownPostDispatchOverwrites.Contains(property.Name))
            {
                continue;
            }

            object? expected = property.GetValue(productionRow);
            object? actual = property.GetValue(testRow);
            if (!Equals(expected, actual))
            {
                yield return $"{typeof(TEntity).Name}.{property.Name}: production={Format(expected)}, nouveau traitement={Format(actual)}";
            }
        }
    }

    // Generic counterpart of RoundTripThroughTestDatabase for the Story 4.1 downstream tables: none of
    // them has a generated identity (natural business key), so the caller supplies how to reload the
    // just-inserted row instead of matching on Id.
    private TEntity RoundTrip<TEntity>(TEntity entity, Func<IQueryable<TEntity>, TEntity> reload)
        where TEntity : class
    {
        using TransactionScope scope = new(TransactionScopeAsyncFlowOption.Enabled);
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.Database.OpenConnection();

        context.Set<TEntity>().Add(entity);
        context.SaveChanges();

        // No scope.Complete(): the insert rolls back here, same as RoundTripThroughTestDatabase.
        return reload(context.Set<TEntity>().AsNoTracking());
    }

    // READ-ONLY production access for the downstream tables - a LINQ query only, never Add/SaveChanges,
    // the same discipline as ReadProductionRows.
    private static TEntity? ReadProductionRow<TEntity>(Func<IQueryable<TEntity>, TEntity?> query)
        where TEntity : class
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer(ProductionConnectionString)
            .Options;

        using AscoLsiDbContext production = new(options);
        return query(production.Set<TEntity>().AsNoTracking());
    }

    // List-returning counterpart of ReadProductionRow, for a table whose key isn't OF alone (the 4
    // L_D_SECTIONCHARGE_* tables) - the caller must handle more than one candidate itself.
    private static List<TEntity> ReadProductionRows<TEntity>(Func<IQueryable<TEntity>, List<TEntity>> query)
        where TEntity : class
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer(ProductionConnectionString)
            .Options;

        using AscoLsiDbContext production = new(options);
        return query(production.Set<TEntity>().AsNoTracking());
    }

    // Trailing spaces on a fixed-width NCHAR column are storage padding, not a data difference.
    private static object? Normalize(object? value) => value is string text ? text.TrimEnd() : value;

    private static string Format(object? value) => value switch
    {
        null => "(null)",
        string text => $"'{text}'",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "(null)",
    };

    private static string Describe(IReadOnlyList<ConversionError> errors) =>
        string.Join(" ; ", errors.Select(e => $"[{e.Code}] {e.Message}"));

    // DateReception is excluded from the comparison, so any fixed instant works; this keeps the mapped
    // Header day-of-year check deterministic too.
    private static TimeProvider StableClock() =>
        new FixedClock(DateTimeOffset.Parse("2026-09-04T08:00:00Z", CultureInfo.InvariantCulture));
}
