using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 4.10 (B-5): same discipline as DownstreamColumnLengthsParityTests, for the DECIMAL(p,s)
// magnitude bound Kape22Persister's pre-SaveChanges guard checks scaled columns against. In scope is
// exactly the set DecimalScale.Apply actually touches - the same 21 call sites MappingAnnexCompleteness
// -Tests' B-1/B-2 gate extracts via MapperScaleCallSites - not every DECIMAL column of these 5 tables:
// L_D_SECTIONCHARGE_LINGOT's 4 PriseDeFer* columns are DECIMAL(4,1) too but stay at their CLR default
// (à_clarifier, never assigned via DecimalScale.Apply - see the mapper's own comment), so they carry no
// overflow risk from a mapper-produced value and no DownstreamColumnMagnitudes entry. Written
// test-first (CC-1), Unit-only.
[Trait("Category", TestCategory.Unit)]
public class DownstreamColumnMagnitudesParityTests
{
    private static readonly Dictionary<string, string> TableByMapperFile = new()
    {
        ["OrdreFabricationMapper.cs"] = "L_D_ORDRE_FABRICATION",
        ["SectionChargeChutageMapper.cs"] = "L_D_SECTIONCHARGE_CHUTAGE",
        ["SectionChargeDecoupeMapper.cs"] = "L_D_SECTIONCHARGE_DECOUPE",
        ["SectionChargeLingotMapper.cs"] = "L_D_SECTIONCHARGE_LINGOT",
        ["SectionChargePitsMapper.cs"] = "L_D_SECTIONCHARGE_PITS",
    };

    // Every column a real DecimalScale.Apply call site targets has a matching DownstreamColumnMagnitudes
    // entry carrying its real DECIMAL(p,s) bound (10^(p-s)), and vice versa. A column name that repeats
    // across two tables (the six Tolerance* columns, shared between L_D_ORDRE_FABRICATION and
    // L_D_SECTIONCHARGE_LINGOT) must carry the same bound in both.
    [Fact]
    [Trait("AC", "4.10-B5")]
    public void DownstreamColumnMagnitudes_MatchGeneratedSchema()
    {
        Dictionary<string, decimal> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string mapperFile, string table) in TableByMapperFile)
        {
            string source = File.ReadAllText(RepoLayout.ProjectFile(Path.Combine("src", "Kape22Importer", mapperFile)));
            Dictionary<string, decimal?> boundsByColumn = SqlTableSchema.Read("01-ascolsi-tables.sql", table)
                .ToDictionary(column => column.Name, column => column.DecimalMagnitude, StringComparer.OrdinalIgnoreCase);

            foreach (MapperScaleCallSite callSite in MapperScaleCallSites.ExtractFrom(source, table))
            {
                decimal bound = boundsByColumn.TryGetValue(callSite.Property, out decimal? magnitude) && magnitude is decimal value
                    ? value
                    : throw new InvalidOperationException($"{table}.{callSite.Property}: no DECIMAL(p,s) column found in the schema.");

                if (expected.TryGetValue(callSite.Property, out decimal existingBound))
                {
                    Assert.True(existingBound == bound, $"{callSite.Property}: {existingBound} in one table, {bound} in {table}.");
                }
                else
                {
                    expected[callSite.Property] = bound;
                }
            }
        }

        Assert.NotEmpty(DownstreamColumnMagnitudes.MaxAbsoluteValues);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            DownstreamColumnMagnitudes.MaxAbsoluteValues.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
    }
}
