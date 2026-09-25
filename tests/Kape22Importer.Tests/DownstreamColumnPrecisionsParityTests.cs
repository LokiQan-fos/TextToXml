using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 4.14 (Épic 4 retro #4, D-2): same discipline as DownstreamColumnLengthsParityTests and
// DownstreamColumnMagnitudesParityTests, for the (precision, scale) map AscoLsiDbContext applies to every
// decimal downstream column. In scope is every DECIMAL(p,s) column of the 9 tables that map feeds -
// L_D_CONSIGNES carries none and gets no precisions. Written test-first (CC-1), Unit-only.
[Trait("Category", TestCategory.Unit)]
public class DownstreamColumnPrecisionsParityTests
{
    private static readonly string[] TableNames =
    [
        "L_D_ORDRE_FABRICATION",
        "L_D_COULEE",
        "L_D_SECTIONCHARGE_CHUTAGE",
        "L_D_SECTIONCHARGE_DECOUPE",
        "L_D_SECTIONCHARGE_LINGOT",
        "L_D_SECTIONCHARGE_PITS",
        "L_D_SECTIONCHARGE_POIDSMETRIQUE",
        "L_D_SECTIONCHARGE_REFROIDISSOIRS",
        "L_D_SECTIONCHARGE_SVT",
    ];

    // Every DECIMAL(p,s) column of the 9 tables has a DownstreamColumnPrecisions entry with the same
    // (precision, scale), and every entry has at least one such column.
    [Fact]
    [Trait("AC", "4.14-AC1")]
    public void DownstreamColumnPrecisions_MatchGeneratedSchema()
    {
        Dictionary<string, (int Precision, int Scale)> expected = ExpectedPrecisions(
            TableNames.SelectMany(table => SqlTableSchema.Read("01-ascolsi-tables.sql", table)
                .Select(column => (table, column))));

        Assert.NotEmpty(expected);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            DownstreamColumnPrecisions.Precisions.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
    }

    // A column name shared by two tables at different (precision, scale) cannot be served by one map
    // keyed by column name, so the failure names both tables.
    [Fact]
    [Trait("AC", "4.14-AC1")]
    public void ExpectedPrecisions_SharedColumnWithDivergentPrecision_ThrowsNamingBothTables()
    {
        SqlColumn narrow = new(typeof(decimal), (2, 1), true, null, "ToleranceMaxSection", "DECIMAL");
        SqlColumn wide = narrow with { DecimalPrecision = (3, 1) };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => ExpectedPrecisions([("TABLE_A", narrow), ("TABLE_B", wide)]));

        Assert.Contains("TABLE_A", exception.Message);
        Assert.Contains("TABLE_B", exception.Message);
    }

    // Every magnitude bound is the 10^(p-s) of the same column's (precision, scale), so the two maps
    // cannot drift apart.
    [Fact]
    [Trait("AC", "4.14-AC2")]
    public void DownstreamColumnMagnitudes_AgreeWithPrecisions()
    {
        Assert.NotEmpty(DownstreamColumnMagnitudes.MaxAbsoluteValues);
        foreach ((string column, decimal bound) in DownstreamColumnMagnitudes.MaxAbsoluteValues)
        {
            Assert.True(
                DownstreamColumnPrecisions.Precisions.TryGetValue(column, out (int Precision, int Scale) precision),
                $"{column}: magnitude entry without a DownstreamColumnPrecisions entry.");
            decimal expectedBound = (decimal)Math.Pow(10, precision.Precision - precision.Scale);
            Assert.True(bound == expectedBound, $"{column}: magnitude {bound}, expected {expectedBound} from ({precision.Precision},{precision.Scale}).");
        }
    }

    private static Dictionary<string, (int Precision, int Scale)> ExpectedPrecisions(
        IEnumerable<(string Table, SqlColumn Column)> columns)
    {
        Dictionary<string, (string Table, (int Precision, int Scale) Precision)> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string table, SqlColumn column) in columns)
        {
            if (column.DecimalPrecision is not (int, int) precision)
            {
                continue;
            }

            if (seen.TryGetValue(column.Name, out (string Table, (int Precision, int Scale) Precision) existing))
            {
                if (existing.Precision != precision)
                {
                    throw new InvalidOperationException(
                        $"{column.Name}: {existing.Precision} in {existing.Table}, {precision} in {table}.");
                }
            }
            else
            {
                seen[column.Name] = (table, precision);
            }
        }

        return seen.ToDictionary(pair => pair.Key, pair => pair.Value.Precision, StringComparer.OrdinalIgnoreCase);
    }
}
