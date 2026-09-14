using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.1: the entities freeze the AscoLSI target tables from Annexe C.1 / C.2 (database-first, no
// migration). These tests read the built EF model, so they need no database and stay in the Unit
// category. CC-1 exempts the scaffolding of Story 2.1 from the strict red-to-green ceremony: the
// assertions exist and fail before the DbContext does, and their failure form is a build or model error.
// AC trait lives per method (not on the class) since Story 4.1 extends this file with its own 10 tables.
[Trait("Category", TestCategory.Unit)]
public class AscoLsiDbContextModelTests
{
    // Annexe C.1: the only NOT NULL columns of L_D_KAPE22 besides the identity Id.
    private static readonly string[] ExpectedRequiredColumns =
    [
        "Client",
        "Coulee",
        "DateReception",
        "Indice",
        "Nuance",
        "NumeroFichier",
        "OF",
        "Type",
    ];

    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_KAPE22_Has92Columns()
    {
        Assert.Equal(92, Kape22Entity().GetProperties().Count());
    }

    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_KAPE22_RequiredColumnsMatchAnnexeC1()
    {
        string[] required = Kape22Entity().GetProperties()
            .Where(property => !property.IsNullable && property.Name != "Id")
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedRequiredColumns, required);
    }

    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_KAPE22_ClrTypesMatchAnnexeC1()
    {
        IEntityType entity = Kape22Entity();

        // Numeric SAP fields (Annexe C.1, PRD D6): int, nullable unless in the NOT NULL set.
        AssertColumn(entity, "Indice", typeof(int), nullable: false);
        AssertColumn(entity, "DiametreProduit", typeof(int), nullable: true);
        AssertColumn(entity, "MatriculeClient", typeof(int), nullable: true);
        AssertColumn(entity, "PriseDeFer", typeof(int), nullable: true);

        // Text fields keep leading zeros, so they are string (Annexe C.1).
        AssertColumn(entity, "NumeroFichier", typeof(string), nullable: false);
        AssertColumn(entity, "Coulee", typeof(string), nullable: false);
        AssertColumn(entity, "OF", typeof(string), nullable: false);
        AssertColumn(entity, "Type", typeof(string), nullable: false);
        AssertColumn(entity, "ProfilProduit", typeof(string), nullable: true);

        // DateReception is derived and NOT NULL; the two DateEnfournement columns stay NULL (PRD D14).
        AssertColumn(entity, "DateReception", typeof(DateTime), nullable: false);
        AssertColumn(entity, "DateEnfournementFour1", typeof(DateTime), nullable: true);
        AssertColumn(entity, "DateEnfournementFour2", typeof(DateTime), nullable: true);
    }

    [Fact]
    [Trait("AC", "2.1")]
    public void L_D_LOG_COMMANDE_ShapeMatchesAnnexeC2()
    {
        IEntityType entity = LogCommandeEntity();

        string[] columns = entity.GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Commande", "Date", "Id", "Message", "NumLingot", "OF", "Trace", "User"],
            columns);

        // Annexe C.2: everything is NOT NULL except Trace.
        Assert.True(entity.FindProperty("Trace")!.IsNullable);
        foreach (string required in new[] { "Commande", "Date", "Message", "NumLingot", "OF", "User" })
        {
            Assert.False(entity.FindProperty(required)!.IsNullable, $"{required} must be NOT NULL.");
        }

        AssertColumn(entity, "NumLingot", typeof(int), nullable: false);
        AssertColumn(entity, "Date", typeof(DateTime), nullable: false);
        AssertColumn(entity, "Trace", typeof(bool), nullable: true);
        AssertColumn(entity, "Commande", typeof(string), nullable: false);
    }

    [Fact]
    [Trait("AC", "2.1")]
    public void BothEntities_UseIdentityForId()
    {
        Assert.Equal(ValueGenerated.OnAdd, Kape22Entity().FindProperty("Id")!.ValueGenerated);
        Assert.Equal(ValueGenerated.OnAdd, LogCommandeEntity().FindProperty("Id")!.ValueGenerated);
    }

    [Fact]
    [Trait("AC", "2.1")]
    [Trait("AC", "4.1")]
    public void Assembly_ContainsNoEfMigration()
    {
        Type[] migrations = typeof(AscoLsiDbContext).Assembly.GetTypes()
            .Where(type => typeof(Migration).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        Assert.True(
            migrations.Length == 0,
            $"AscoLsiDbContext is database-first (AR-8): no migration expected, found {string.Join(", ", migrations.Select(m => m.Name))}.");
    }

    // Story 4.1: none of the 10 downstream tables has an identity column in AFV004-LSI (sys.columns,
    // 2026-09-14); every key is a natural, composite business key. This guards against a surrogate Id
    // being added by mistake and pins the exact composite key columns, in order.
    [Theory]
    [Trait("AC", "4.1")]
    [MemberData(nameof(DownstreamTableShapes))]
    public void DownstreamEntity_HasNoIdentityAndTheExpectedCompositeKey(
        Type entityType, int expectedColumnCount, string[] expectedPrimaryKeyColumns)
    {
        IEntityType entity = Model().FindEntityType(entityType)!;

        Assert.Equal(expectedColumnCount, entity.GetProperties().Count());
        Assert.Null(entity.FindProperty("Id"));

        string[] primaryKeyColumns = entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray();
        Assert.Equal(expectedPrimaryKeyColumns, primaryKeyColumns);

        foreach (IProperty property in entity.GetProperties())
        {
            Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        }
    }

    public static IEnumerable<object[]> DownstreamTableShapes()
    {
        yield return [typeof(L_D_ORDRE_FABRICATION), 45, new[] { "OF" }];
        yield return [typeof(L_D_COULEE), 78, new[] { "IdCoulee" }];
        yield return [typeof(L_D_CONSIGNES), 7, new[] { "OF", "CodeOperation", "TypeConsigne", "ConsigneGPAO" }];
        yield return [typeof(L_D_SECTIONCHARGE_CHUTAGE), 6, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_DECOUPE), 5, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_LINGOT), 19, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_PITS), 10, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_POIDSMETRIQUE), 3, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_REFROIDISSOIRS), 21, new[] { "OF", "CodeOperation" }];
        yield return [typeof(L_D_SECTIONCHARGE_SVT), 3, new[] { "OF", "CodeOperation" }];
    }

    // Compares against the underlying CLR type so a nullable value type ("int?") matches "typeof(int)"
    // regardless of how the EF version surfaces IProperty.ClrType.
    private static void AssertColumn(IEntityType entity, string propertyName, Type underlyingType, bool nullable)
    {
        IProperty? property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(underlyingType, Nullable.GetUnderlyingType(property!.ClrType) ?? property.ClrType);
        Assert.Equal(nullable, property.IsNullable);
    }

    private static IEntityType Kape22Entity() => Model().FindEntityType(typeof(L_D_KAPE22))!;

    private static IEntityType LogCommandeEntity() => Model().FindEntityType(typeof(L_D_LOG_COMMANDE))!;

    private static IModel Model()
    {
        // A syntactically valid but unused connection string: building the model opens no connection.
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);
        return context.Model;
    }
}
