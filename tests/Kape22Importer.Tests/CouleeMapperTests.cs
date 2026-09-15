using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.3 (FR-18): CouleeMapper.Map(L_D_KAPE22) -> L_D_COULEE, coded explicitly from the Story 4.2
// annex (annexe-mapping-dispatch-epic4.md § L_D_COULEE), no reflection, no database access - a pure
// function (AD-2). Written test-first (CC-1): red until CouleeMapper ships. Unit-only (AR-12) -
// epics.md marks CC-6/CC-7 not applicable to this mapper, there is no database access to exercise.
// The annex documents almost the entire table as à_clarifier (no P60 dispatch equivalent found in the
// legacy Coulee.xml/CouleeManager.cs); only 5 columns carry a sourced or ruled value.
[Trait("Category", TestCategory.Unit)]
public class CouleeMapperTests
{
    // The five columns the annex explicitly documents (sourced or ruled), plus the two à_clarifier NOT
    // NULL columns forced to a CLR default - everything L_D_COULEE has besides the ~70 untouched
    // à_clarifier columns swept by Map_EveryUndocumentedColumn_StaysAtClrDefault_AcFr18_4.
    private static readonly string[] DocumentedColumns =
    [
        nameof(L_D_COULEE.DateReception),
        nameof(L_D_COULEE.DerniereModif),
        nameof(L_D_COULEE.EtatReception),
        nameof(L_D_COULEE.Externe),
        nameof(L_D_COULEE.IdCoulee),
        nameof(L_D_COULEE.NbLingotRestantARefroidir),
        nameof(L_D_COULEE.Nuance),
    ];

    // AC-FR18-3: IdCoulee is the annex's one renamed field - the OF's Coulee (same source as
    // L_D_ORDRE_FABRICATION.Coulee), not a homonymous copy.
    [Fact]
    [Trait("AC", "FR18-3")]
    public void Map_ValidSource_MapsIdCouleeFromKape22Coulee_AcFr18_3()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_COULEE entity = CouleeMapper.Map(source);

        Assert.Equal(source.Coulee, entity.IdCoulee);
    }

    // AC-FR18-3: Nuance is a homonymous sourced copy.
    [Fact]
    [Trait("AC", "FR18-3")]
    public void Map_ValidSource_CopiesNuance_AcFr18_3()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_COULEE entity = CouleeMapper.Map(source);

        Assert.Equal(source.Nuance, entity.Nuance);
    }

    // AC-FR18-3: Externe is always false for a Coulee created by a KAPE22 dispatch - true is only
    // reachable through legacy administrative scenarios the P60 flow never calls (CreateDefaultFroid /
    // CreateFakeBUL, annex evidence).
    [Fact]
    [Trait("AC", "FR18-3")]
    public void Map_ValidSource_SetsExterneToFalse_AcFr18_3()
    {
        L_D_COULEE entity = CouleeMapper.Map(ReferenceKape22());

        Assert.False(entity.Externe);
    }

    // AC-FR18-3: DateReception/DerniereModif are the import timestamp, taken from the injected clock in
    // Paris local time - the same audit-timestamp convention OrdreFabricationMapper and every other
    // TimeProvider consumer in Kape22Importer already follow.
    [Fact]
    [Trait("AC", "FR18-3")]
    public void Map_ValidSource_StampsDateReceptionAndDerniereModifFromClockInParisTime_AcFr18_3()
    {
        L_D_COULEE entity = CouleeMapper.Map(ReferenceKape22(), WinterClock());

        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), entity.DateReception);
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), entity.DerniereModif);
    }

    // AC-FR18-2 (shared purity requirement, restated for Coulee by AC-FR18-3's "fonction pure
    // également"): CouleeMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR18-3")]
    public void CouleeMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr18_3()
    {
        string source = MapperSourceText("CouleeMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR18-4: EtatReception and NbLingotRestantARefroidir are NOT NULL int columns the annex flags
    // à_clarifier with no initial value found for a P60 dispatch - left at the CLR default 0 rather than
    // a guessed enum state or count.
    [Theory]
    [InlineData(nameof(L_D_COULEE.EtatReception))]
    [InlineData(nameof(L_D_COULEE.NbLingotRestantARefroidir))]
    [Trait("AC", "FR18-4")]
    public void Map_ToClarifyNotNullColumn_StaysAtZero_AcFr18_4(string columnName)
    {
        L_D_COULEE entity = CouleeMapper.Map(ReferenceKape22());

        Assert.Equal(0, (int)typeof(L_D_COULEE).GetProperty(columnName)!.GetValue(entity)!);
    }

    // AC-FR18-4: every other L_D_COULEE column - the ~70 à_clarifier ones with no P60 source identified
    // in the annex - is left untouched at its CLR default (null, since every one of them is nullable).
    [Fact]
    [Trait("AC", "FR18-4")]
    public void Map_EveryUndocumentedColumn_StaysAtClrDefault_AcFr18_4()
    {
        L_D_COULEE entity = CouleeMapper.Map(ReferenceKape22());

        List<string> offenders = [];
        foreach (PropertyInfo property in typeof(L_D_COULEE).GetProperties()
            .Where(property => !DocumentedColumns.Contains(property.Name)))
        {
            object? expectedDefault = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;
            if (!Equals(expectedDefault, property.GetValue(entity)))
            {
                offenders.Add(property.Name);
            }
        }

        Assert.True(offenders.Count == 0, "Unexpected non-default value on: " + string.Join(", ", offenders));
    }

    // AC-FR18-4: the mapper documents the à_clarifier columns it leaves untouched with the same
    // "assumed, unverified" marker citing deferred-work.md as Kape22Mapper does (AC-FR7-2 precedent),
    // instead of leaving the near-total gap silent.
    [Fact]
    [Trait("AC", "FR18-4")]
    public void CouleeMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr18_4()
    {
        string source = MapperSourceText("CouleeMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
