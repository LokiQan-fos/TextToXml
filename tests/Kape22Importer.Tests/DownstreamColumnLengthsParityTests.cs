using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 4.1: same discipline as Kape22ColumnLengthsParityTests (Story 2.5, AC-FR8-2, risk R-6), extended
// to the 10 downstream dispatch tables. DownstreamColumnLengths.MaxLengths is one map shared across all
// 10 tables, so the first test also checks that no column name repeats with two different lengths across
// tables before comparing against the constants. Written test-first (CC-1), Unit-only.
[Trait("Category", TestCategory.Unit)]
public class DownstreamColumnLengthsParityTests
{
    private static readonly Type[] EntityTypes =
    [
        typeof(L_D_ORDRE_FABRICATION),
        typeof(L_D_COULEE),
        typeof(L_D_CONSIGNES),
        typeof(L_D_SECTIONCHARGE_CHUTAGE),
        typeof(L_D_SECTIONCHARGE_DECOUPE),
        typeof(L_D_SECTIONCHARGE_LINGOT),
        typeof(L_D_SECTIONCHARGE_PITS),
        typeof(L_D_SECTIONCHARGE_POIDSMETRIQUE),
        typeof(L_D_SECTIONCHARGE_REFROIDISSOIRS),
        typeof(L_D_SECTIONCHARGE_SVT),
    ];

    private static readonly string[] TableNames =
    [
        "L_D_ORDRE_FABRICATION",
        "L_D_COULEE",
        "L_D_CONSIGNES",
        "L_D_SECTIONCHARGE_CHUTAGE",
        "L_D_SECTIONCHARGE_DECOUPE",
        "L_D_SECTIONCHARGE_LINGOT",
        "L_D_SECTIONCHARGE_PITS",
        "L_D_SECTIONCHARGE_POIDSMETRIQUE",
        "L_D_SECTIONCHARGE_REFROIDISSOIRS",
        "L_D_SECTIONCHARGE_SVT",
    ];

    // Every bounded string column of the 10 tables has a matching DownstreamColumnLengths entry with the
    // same character length, and vice versa. A column name that repeats across two tables (OF,
    // CodeOperation, RangOperation, Nuance) must carry the same length in both, or the shared map cannot
    // be correct for at least one of them.
    [Fact]
    [Trait("AC", "4.1")]
    public void DownstreamColumnLengths_MatchGeneratedSchema()
    {
        Dictionary<string, int> expected = new(StringComparer.OrdinalIgnoreCase);
        foreach (string table in TableNames)
        {
            foreach (SqlColumn column in SqlTableSchema.Read("01-ascolsi-tables.sql", table)
                .Where(column => column.ClrType == typeof(string) && column.MaxLength is not null))
            {
                if (expected.TryGetValue(column.Name, out int existingLength))
                {
                    Assert.True(
                        existingLength == column.MaxLength!.Value,
                        $"{column.Name}: {existingLength} in one table, {column.MaxLength} in {table}.");
                }
                else
                {
                    expected[column.Name] = column.MaxLength!.Value;
                }
            }
        }

        Assert.NotEmpty(DownstreamColumnLengths.MaxLengths);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            DownstreamColumnLengths.MaxLengths.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
    }

    // AscoLsiDbContext applies each constant that exists on a given entity with HasMaxLength, so a
    // read of the built model reflects the same lengths without touching the database.
    [Fact]
    [Trait("AC", "4.1")]
    public void DownstreamColumnLengths_AreAppliedToTheEfModel()
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;
        using AscoLsiDbContext context = new(options);

        foreach (Type entityType in EntityTypes)
        {
            IEntityType entity = context.Model.FindEntityType(entityType)!;
            foreach (IProperty property in entity.GetProperties())
            {
                if (DownstreamColumnLengths.MaxLengths.TryGetValue(property.Name, out int expectedLength))
                {
                    Assert.Equal(expectedLength, property.GetMaxLength());
                }
            }
        }
    }
}
