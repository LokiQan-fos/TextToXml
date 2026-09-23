using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargePitsMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_PITS?, coded explicitly
// from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_PITS), no reflection, no
// database access - a pure function (AD-2). Written test-first (CC-1): red until SectionChargePitsMapper
// ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The reference Fichier's Pits
// section is applicable (CodeOpePits/RangOpePits both populated). DateEnfournementFour1/2 are
// deliberately never sourced (legacy "Enlevé car empêche d'enfourner"); DateDefournementFour1/2 stay
// à_clarifier (no source identified).
[Trait("Category", TestCategory.Unit)]
public class SectionChargePitsMapperTests
{
    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_SECTIONCHARGE_PITS? entity = SectionChargePitsMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpePits, entity!.CodeOperation);
        Assert.Equal(DecimalScale.Apply(source.H2Coulee, 1), entity.H2Coulee);
        Assert.Equal(source.NumeroFour1, entity.NumeroFour1);
        Assert.Equal(source.NumeroFour2, entity.NumeroFour2);
        Assert.Equal(DownstreamOf.Pad(source.OF), entity.OF);
        Assert.Equal(source.RangOpePits, entity.RangOperation);
    }

    // AC-FR19-4: DateEnfournementFour1/2 stay NULL by rule - deliberately never sourced from KAPE22
    // (legacy "Enlevé car empêche d'enfourner"; also always null on L_D_KAPE22 itself, D14/AC-FR9-5).
    [Theory]
    [InlineData(nameof(L_D_SECTIONCHARGE_PITS.DateEnfournementFour1))]
    [InlineData(nameof(L_D_SECTIONCHARGE_PITS.DateEnfournementFour2))]
    [Trait("AC", "FR19-4")]
    public void Map_RuleColumnNeverSourced_StaysNull_AcFr19_4(string columnName)
    {
        L_D_SECTIONCHARGE_PITS entity = SectionChargePitsMapper.Map(ReferenceKape22())!;

        Assert.Null(typeof(L_D_SECTIONCHARGE_PITS).GetProperty(columnName)!.GetValue(entity));
    }

    // AC-FR19-4: DateDefournementFour1/2 stay at their CLR default (null) - no source identified in the
    // Story 4.2 annex for a P60 dispatch.
    [Theory]
    [InlineData(nameof(L_D_SECTIONCHARGE_PITS.DateDefournementFour1))]
    [InlineData(nameof(L_D_SECTIONCHARGE_PITS.DateDefournementFour2))]
    [Trait("AC", "FR19-4")]
    public void Map_ToClarifyColumn_StaysAtClrDefault_AcFr19_4(string columnName)
    {
        L_D_SECTIONCHARGE_PITS entity = SectionChargePitsMapper.Map(ReferenceKape22())!;

        Assert.Null(typeof(L_D_SECTIONCHARGE_PITS).GetProperty(columnName)!.GetValue(entity));
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_PITS column is accounted for - the 6 sourced above, the 2 rule-
    // null columns, and the 2 à_clarifier columns - fails if a future column is added without coverage.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargePitsMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_PITS.CodeOperation),
            nameof(L_D_SECTIONCHARGE_PITS.DateDefournementFour1),
            nameof(L_D_SECTIONCHARGE_PITS.DateDefournementFour2),
            nameof(L_D_SECTIONCHARGE_PITS.DateEnfournementFour1),
            nameof(L_D_SECTIONCHARGE_PITS.DateEnfournementFour2),
            nameof(L_D_SECTIONCHARGE_PITS.H2Coulee),
            nameof(L_D_SECTIONCHARGE_PITS.NumeroFour1),
            nameof(L_D_SECTIONCHARGE_PITS.NumeroFour2),
            nameof(L_D_SECTIONCHARGE_PITS.OF),
            nameof(L_D_SECTIONCHARGE_PITS.RangOperation),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_PITS).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-2: the annex's per-OF applicability rule - CodeOpePits empty means the section does not
    // concern this OF, so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpePits", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargePitsMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpePits.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpePits", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargePitsMapper.Map(result.Value!));
    }

    // AC-FR19-1: SectionChargePitsMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargePitsMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargePitsMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR19-4: the mapper documents the à_clarifier columns it leaves untouched with the same
    // "assumed, unverified" marker citing deferred-work.md, instead of leaving the gap silent.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void SectionChargePitsMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr19_4()
    {
        string source = MapperSourceText("SectionChargePitsMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
