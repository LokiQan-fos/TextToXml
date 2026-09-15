using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargeLingotMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_LINGOT?, coded
// explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_LINGOT), no
// reflection, no database access - a pure function (AD-2). Written test-first (CC-1): red until
// SectionChargeLingotMapper ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The
// reference Fichier's Lingot section is applicable (CodeOpeLingot/RangOpeLingot both populated). The
// annex leaves the 8 PriseDeFer*/Programme* columns à_clarifier - a database-lookup computation, out of a
// pure mapper's reach (AD-2).
[Trait("Category", TestCategory.Unit)]
public class SectionChargeLingotMapperTests
{
    private static readonly string[] ToClarifyColumns =
    [
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerEpaisseur),
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerEpaisseurGPAO),
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerHauteur),
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerHauteurGPAO),
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerSection),
        nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFerSectionGPAO),
        nameof(L_D_SECTIONCHARGE_LINGOT.Programme),
        nameof(L_D_SECTIONCHARGE_LINGOT.ProgrammeGPAO),
    ];

    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_SECTIONCHARGE_LINGOT? entity = SectionChargeLingotMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpeLingot, entity!.CodeOperation);
        Assert.Equal((decimal?)source.EpaisseurEnLaminage, entity.EpaisseurEnLaminage);
        Assert.Equal(source.OF, entity.OF);
        Assert.Equal(source.PriseDeFer, entity.PriseDeFer);
        Assert.Equal(source.ProfileLamine, entity.ProfileLamine);
        Assert.Equal(source.RangOpeLingot, entity.RangOperation);
        Assert.Equal((decimal?)source.SectionLaminage, entity.SectionLaminage);
        Assert.Equal((decimal?)source.ToleranceMaxEpaisseur1, entity.ToleranceMaxEpaisseur);
        Assert.Equal((decimal?)source.ToleranceMaxSection1, entity.ToleranceMaxSection);
        Assert.Equal((decimal?)source.ToleranceMinEpaisseur1, entity.ToleranceMinEpaisseur);
        Assert.Equal((decimal?)source.ToleranceMinSection1, entity.ToleranceMinSection);
    }

    // AC-FR19-4: the 8 PriseDeFer*/Programme* columns the annex leaves à_clarifier stay at their CLR
    // default rather than a guessed lookup result.
    [Theory]
    [MemberData(nameof(ToClarifyColumnsData))]
    [Trait("AC", "FR19-4")]
    public void Map_ToClarifyColumn_StaysAtClrDefault_AcFr19_4(string columnName)
    {
        L_D_SECTIONCHARGE_LINGOT entity = SectionChargeLingotMapper.Map(ReferenceKape22())!;

        Assert.Null(typeof(L_D_SECTIONCHARGE_LINGOT).GetProperty(columnName)!.GetValue(entity));
    }

    public static TheoryData<string> ToClarifyColumnsData() => [.. ToClarifyColumns];

    // AC-FR19-1: every L_D_SECTIONCHARGE_LINGOT column is either explicitly sourced above or one of the
    // 8 à_clarifier columns - fails if a future column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeLingotMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] sourced =
        [
            nameof(L_D_SECTIONCHARGE_LINGOT.CodeOperation),
            nameof(L_D_SECTIONCHARGE_LINGOT.EpaisseurEnLaminage),
            nameof(L_D_SECTIONCHARGE_LINGOT.OF),
            nameof(L_D_SECTIONCHARGE_LINGOT.PriseDeFer),
            nameof(L_D_SECTIONCHARGE_LINGOT.ProfileLamine),
            nameof(L_D_SECTIONCHARGE_LINGOT.RangOperation),
            nameof(L_D_SECTIONCHARGE_LINGOT.SectionLaminage),
            nameof(L_D_SECTIONCHARGE_LINGOT.ToleranceMaxEpaisseur),
            nameof(L_D_SECTIONCHARGE_LINGOT.ToleranceMaxSection),
            nameof(L_D_SECTIONCHARGE_LINGOT.ToleranceMinEpaisseur),
            nameof(L_D_SECTIONCHARGE_LINGOT.ToleranceMinSection),
        ];
        List<string> documented = [.. sourced, .. ToClarifyColumns];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_LINGOT).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-2: the annex's per-OF applicability rule - CodeOpeLingot empty means the section does not
    // concern this OF, so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpeLingot", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeLingotMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpeLingot.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpeLingot", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeLingotMapper.Map(result.Value!));
    }

    // AC-FR19-1: SectionChargeLingotMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeLingotMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargeLingotMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR19-4: the mapper documents the à_clarifier columns it leaves untouched with the same
    // "assumed, unverified" marker citing deferred-work.md, instead of leaving the gap silent.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void SectionChargeLingotMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr19_4()
    {
        string source = MapperSourceText("SectionChargeLingotMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
