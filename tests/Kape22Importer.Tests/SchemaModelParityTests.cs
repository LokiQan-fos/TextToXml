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
// column names, nullability, CLR type family and the datetime store type between the two. Column
// string lengths stay out of scope until Story 2.5. No database is needed.
[Trait("Category", TestCategory.Unit)]
[Trait("AC", "2.1")]
public class SchemaModelParityTests
{
    [Fact]
    public void L_D_KAPE22_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_KAPE22"),
            EntityType(typeof(L_D_KAPE22)));
    }

    [Fact]
    public void L_D_LOG_COMMANDE_ModelMatchesGeneratedSchema()
    {
        AssertParity(
            SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_LOG_COMMANDE"),
            EntityType(typeof(L_D_LOG_COMMANDE)));
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
