using System;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.3 (FR-18): OrdreFabricationMapper.Map(L_D_KAPE22) -> L_D_ORDRE_FABRICATION, coded explicitly
// from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_ORDRE_FABRICATION), no reflection, no
// database access - a pure function (AD-2). Written test-first (CC-1): red until OrdreFabricationMapper
// ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable to this mapper, there is no
// database access to exercise.
[Trait("Category", TestCategory.Unit)]
public class OrdreFabricationMapperTests
{
    // Every annex "sourcée" column: copies its homonymous L_D_KAPE22 field verbatim, widened to decimal
    // where the target column is decimal and the source is int?. Names in alphabetical order (CC-4),
    // matching the annex table.
    public static TheoryData<string> SourcedColumns() =>
    [
        nameof(L_D_ORDRE_FABRICATION.AcompteSolde),
        nameof(L_D_ORDRE_FABRICATION.ClasseDeChute),
        nameof(L_D_ORDRE_FABRICATION.Client),
        nameof(L_D_ORDRE_FABRICATION.CodeDemiProduit),
        nameof(L_D_ORDRE_FABRICATION.Coulee),
        nameof(L_D_ORDRE_FABRICATION.DiametreProduit),
        nameof(L_D_ORDRE_FABRICATION.Epaisseur),
        nameof(L_D_ORDRE_FABRICATION.Indice),
        nameof(L_D_ORDRE_FABRICATION.LongueurCD),
        nameof(L_D_ORDRE_FABRICATION.MarqueCommerciale),
        nameof(L_D_ORDRE_FABRICATION.NombreDemiProduit),
        nameof(L_D_ORDRE_FABRICATION.Nuance),
        nameof(L_D_ORDRE_FABRICATION.NumeroFichier),
        nameof(L_D_ORDRE_FABRICATION.NumeroMontage),
        nameof(L_D_ORDRE_FABRICATION.OF),
        nameof(L_D_ORDRE_FABRICATION.PoidsDemiProduitUnitaire),
        nameof(L_D_ORDRE_FABRICATION.PoidsPrevuDemiProduit),
        nameof(L_D_ORDRE_FABRICATION.ProfilProduit),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMaxEpaisseur),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMaxLongueur),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMaxSection),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMinEpaisseur),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMinLongueur),
        nameof(L_D_ORDRE_FABRICATION.ToleranceMinSection),
        nameof(L_D_ORDRE_FABRICATION.Type),
    ];

    [Theory]
    [MemberData(nameof(SourcedColumns))]
    [Trait("AC", "FR18-1")]
    public void Map_SourcedColumn_CopiesHomonymousKape22Field_AcFr18_1(string columnName)
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_ORDRE_FABRICATION entity = OrdreFabricationMapper.Map(source);

        object? expected = Widen(typeof(L_D_KAPE22).GetProperty(columnName)!.GetValue(source));
        object? actual = Widen(typeof(L_D_ORDRE_FABRICATION).GetProperty(columnName)!.GetValue(entity));
        Assert.Equal(expected, actual);
    }

    // Every annex "règle" column that stays NULL at dispatch: each is only positioned by a later,
    // non-P60 event per the annex (OrdreDeFabricationManager / OrdreFabricationController line refs).
    [Theory]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.DateDebut))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.DateDebutLaminage))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.DateEVC))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.DateFin))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.DateFinLaminage))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.TemperatureScarfing))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.TemperatureT03))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.TemperatureT07))]
    [Trait("AC", "FR18-1")]
    public void Map_RuleColumnPositionedByALaterEvent_StaysNull_AcFr18_1(string columnName)
    {
        L_D_ORDRE_FABRICATION entity = OrdreFabricationMapper.Map(ReferenceKape22());

        Assert.Null(typeof(L_D_ORDRE_FABRICATION).GetProperty(columnName)!.GetValue(entity));
    }

    // The annex's other "règle" pair: DateMaj/DateReception are the import timestamp, taken from the
    // injected clock in Paris local time - the same audit-timestamp convention every other TimeProvider
    // consumer in Kape22Importer already follows (DerivedFields, InboxScanner, Kape22FichierProcessor,
    // Kape22Persister).
    [Fact]
    [Trait("AC", "FR18-1")]
    public void Map_ValidSource_StampsDateMajAndDateReceptionFromClockInParisTime_AcFr18_1()
    {
        L_D_ORDRE_FABRICATION entity = OrdreFabricationMapper.Map(ReferenceKape22(), WinterClock());

        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), entity.DateMaj);
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), entity.DateReception);
    }

    // AC-FR18-2: OrdreFabricationMapper is a pure function - no reflection, no database access. A
    // compile-barrier check (CC-1's "test-barrière-à-la-compilation" family, like FormatIsolationTests):
    // its failure form is a structural assertion, not a red-to-green behavioural cycle.
    [Fact]
    [Trait("AC", "FR18-2")]
    public void OrdreFabricationMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr18_2()
    {
        string source = MapperSourceText("OrdreFabricationMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR18-4: the annex's à_clarifier columns with no legacy source identified for a P60 dispatch are
    // left at their CLR default instead of a guessed value.
    [Theory]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.Etat))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.NombreLingotsWagon1Four1))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.NombreLingotsWagon1Four2))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.NombreLingotsWagon2Four1))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.NombreLingotsWagon2Four2))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.OFOrigine))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.PoidsPesee))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.SensLaminage))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.SensLaminageGPAO))]
    [InlineData(nameof(L_D_ORDRE_FABRICATION.SuiviDeZoneZone))]
    [Trait("AC", "FR18-4")]
    public void Map_ToClarifyColumn_StaysAtClrDefault_AcFr18_4(string columnName)
    {
        L_D_ORDRE_FABRICATION entity = OrdreFabricationMapper.Map(ReferenceKape22());

        PropertyInfo property = typeof(L_D_ORDRE_FABRICATION).GetProperty(columnName)!;
        object? expectedDefault = property.PropertyType.IsValueType
            ? Activator.CreateInstance(property.PropertyType)
            : null;
        Assert.Equal(expectedDefault, property.GetValue(entity));
    }

    // AC-FR18-4: the mapper documents each guess-free à_clarifier column with the same "assumed,
    // unverified" marker citing deferred-work.md as Kape22Mapper does (AC-FR7-2 precedent), instead of
    // leaving the omission silent.
    [Fact]
    [Trait("AC", "FR18-4")]
    public void OrdreFabricationMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr18_4()
    {
        string source = MapperSourceText("OrdreFabricationMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    // A numeric CLR value widened to decimal so an int? source and a decimal/decimal? target compare
    // equal regardless of which of the two the annex's target column happens to be; every other value
    // (string, already decimal, null) passes through unchanged.
    private static object? Widen(object? value) => value is int i ? (decimal)i : value;

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
