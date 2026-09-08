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

        MapResult<L_D_KAPE22> mapping = Kape22Mapper.Map(conversion.Xml!, fichierName, StableClock());
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
