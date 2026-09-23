using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargeSvtMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_SVT?, coded explicitly
// from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_SVT), no reflection, no
// database access - a pure function (AD-2). Written test-first (CC-1): red until SectionChargeSvtMapper
// ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The reference Fichier's SVT section
// is NOT applicable (CodeOpeSVT/RangOpeSVT both blank), the natural "non-applicable" fixture; a mutated
// copy exercises the applicable path.
[Trait("Category", TestCategory.Unit)]
public class SectionChargeSvtMapperTests
{
    // AC-FR19-2: the reference Fichier's SVT section does not concern this OF - both applicability
    // fields are blank - so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_ReferenceOF_NotApplicable_ReturnsNull_AcFr19_2()
    {
        Assert.Null(SectionChargeSvtMapper.Map(ReferenceKape22()));
    }

    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpeSVT", "SV1");
            SetChamp(document, "message", "RangOpeSVT", "170");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;

        L_D_SECTIONCHARGE_SVT? entity = SectionChargeSvtMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpeSVT, entity!.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), entity.OF);
        Assert.Equal(source.RangOpeSVT, entity.RangOperation);
    }

    // AC-FR19-2: same rule on the other applicability field, CodeOpeSVT - RangOpeSVT alone does not
    // make the section applicable.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpeSVT", "170"));
        Assert.True(result.Success);

        Assert.Null(SectionChargeSvtMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpeSVT.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpeSVT", "SV1"));
        Assert.True(result.Success);

        Assert.Null(SectionChargeSvtMapper.Map(result.Value!));
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_SVT column is explicitly sourced above - fails if a future
    // column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeSvtMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_SVT.CodeOperation),
            nameof(L_D_SECTIONCHARGE_SVT.OF),
            nameof(L_D_SECTIONCHARGE_SVT.RangOperation),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_SVT).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-1: SectionChargeSvtMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeSvtMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargeSvtMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
