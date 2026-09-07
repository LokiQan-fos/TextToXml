using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.5 (AC-FR8-2, risk R-6): the max_length values the startup compatibility check compares a
// Champ Size against come from Kape22ColumnLengths constants carried by the EF entity configuration,
// never a live sys.columns query. These tests lock those constants to scripts/schema/ (generated from
// AFV004-LSI) and check they reach the model through HasMaxLength. Written test-first (CC-1), Unit-only.
[Trait("Category", TestCategory.Unit)]
public class Kape22ColumnLengthsParityTests
{
    // AC-FR8-2: every bounded string column of scripts/schema/ has a matching Kape22ColumnLengths entry
    // with the same character length, and vice versa. NVARCHAR(MAX) columns (NumeroFichier) carry no entry.
    [Fact]
    [Trait("AC", "FR8-2")]
    public void Kape22ColumnLengths_MatchGeneratedSchema_AcFr8_2()
    {
        Dictionary<string, int> expected = SqlTableSchema.Read("01-ascolsi-tables.sql", "L_D_KAPE22")
            .Where(column => column.ClrType == typeof(string) && column.MaxLength is not null)
            .ToDictionary(column => column.Name, column => column.MaxLength!.Value, StringComparer.Ordinal);

        Assert.NotEmpty(Kape22ColumnLengths.MaxLengths);
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            Kape22ColumnLengths.MaxLengths.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    // AC-FR8-2: AscoLsiDbContext applies each constant to the model with HasMaxLength, so the startup
    // check can read it back from the built model without touching the database.
    [Fact]
    [Trait("AC", "FR8-2")]
    public void Kape22ColumnLengths_AreAppliedToTheEfModel_AcFr8_2()
    {
        IEntityType entity = Kape22Entity();

        foreach ((string column, int length) in Kape22ColumnLengths.MaxLengths)
        {
            Assert.Equal(length, entity.FindProperty(column)!.GetMaxLength());
        }

        // NumeroFichier is NVARCHAR(MAX): it must carry no bound.
        Assert.Null(entity.FindProperty("NumeroFichier")!.GetMaxLength());
    }

    private static IEntityType Kape22Entity()
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);
        return context.Model.FindEntityType(typeof(L_D_KAPE22))!;
    }
}
