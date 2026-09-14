using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Risk R-3: the EF model and the generated scripts/schema/*.sql (scripted from AFV004-LSI) must not
// drift apart, otherwise the integration tests would pass against a fake schema. This test compares
// column names, nullability, CLR type family and the datetime store type between the two. Column string
// lengths are checked separately, per *ColumnLengths class and its own parity test (Kape22ColumnLengths
// / Kape22ColumnLengthsParityTests, DownstreamColumnLengths / DownstreamColumnLengthsParityTests). No
// database is needed here. AC trait lives per method (not on the class) since Story 4.1 extends this
// same mechanism to its own 10 tables below.
[Trait("Category", TestCategory.Unit)]
public class SchemaModelParityTests
{
    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_KAPE22_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_KAPE22"),
            EntityType(typeof(L_D_KAPE22)));
    }

    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_LOG_COMMANDE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_LOG_COMMANDE"),
            EntityType(typeof(L_D_LOG_COMMANDE)));
    }

    // Story 4.1: same parity mechanism, extended to the 10 downstream tables (AFV004-LSI sys.columns,
    // 2026-09-14). One fact per table so a failure names the table without inspecting a combined list.
    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_ORDRE_FABRICATION_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_ORDRE_FABRICATION"),
            EntityType(typeof(L_D_ORDRE_FABRICATION)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_COULEE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_COULEE"),
            EntityType(typeof(L_D_COULEE)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_CONSIGNES_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_CONSIGNES"),
            EntityType(typeof(L_D_CONSIGNES)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_CHUTAGE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_CHUTAGE"),
            EntityType(typeof(L_D_SECTIONCHARGE_CHUTAGE)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_DECOUPE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_DECOUPE"),
            EntityType(typeof(L_D_SECTIONCHARGE_DECOUPE)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_LINGOT_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_LINGOT"),
            EntityType(typeof(L_D_SECTIONCHARGE_LINGOT)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_PITS_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_PITS"),
            EntityType(typeof(L_D_SECTIONCHARGE_PITS)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_POIDSMETRIQUE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_POIDSMETRIQUE"),
            EntityType(typeof(L_D_SECTIONCHARGE_POIDSMETRIQUE)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_REFROIDISSOIRS_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_REFROIDISSOIRS"),
            EntityType(typeof(L_D_SECTIONCHARGE_REFROIDISSOIRS)));
    }

    [Fact]
    [Trait("AC", "4.1")]
    public void L_D_SECTIONCHARGE_SVT_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_SECTIONCHARGE_SVT"),
            EntityType(typeof(L_D_SECTIONCHARGE_SVT)));
    }

    private static void AssertParity(IReadOnlyList<SqlColumn> schema, IEntityType entity)
    {
        Dictionary<string, IProperty> modelColumns = entity.GetProperties()
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        string[] schemaNames = schema.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] modelNames = modelColumns.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(schemaNames, modelNames);

        List<string> mismatches = [];
        foreach (SqlColumn column in schema)
        {
            IProperty property = modelColumns[column.Name];

            // The identity Id is NOT NULL in SQL but modelled as store-generated; nullability is not
            // meaningful to compare there.
            if (column.Name != "Id" && property.IsNullable != column.IsNullable)
            {
                mismatches.Add($"{column.Name}: SQL nullable={column.IsNullable}, model nullable={property.IsNullable}.");
            }

            Type modelUnderlying = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (modelUnderlying != column.ClrType)
            {
                mismatches.Add($"{column.Name}: SQL type {column.ClrType.Name}, model type {modelUnderlying.Name}.");
            }

            // EF maps DateTime to datetime2 by default; the real columns are legacy datetime. Lock the
            // store type so that drift is caught here rather than only at runtime.
            if (column.SqlType is "DATETIME" or "DATE" or "DATETIME2")
            {
                string? modelStoreType = property.GetColumnType();
                if (!string.Equals(modelStoreType, column.SqlType, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"{column.Name}: SQL store type {column.SqlType}, model store type {modelStoreType ?? "(provider default)"}.");
                }
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static IEntityType EntityType(Type clrType)
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);
        return context.Model.FindEntityType(clrType)!;
    }
}
