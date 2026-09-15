using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargePoidsMetriqueMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_POIDSMETRIQUE?,
// coded explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md §
// L_D_SECTIONCHARGE_POIDSMETRIQUE), no reflection, no database access - a pure function (AD-2). Written
// test-first (CC-1): red until SectionChargePoidsMetriqueMapper ships. Unit-only (AR-12) - epics.md marks
// CC-6/CC-7 not applicable. The reference Fichier's PoidsMetrique section is NOT applicable
// (CodeOpePoidMetrique/RangOpePoidMetrique both blank), the natural "non-applicable" fixture; a mutated
// copy exercises the applicable path.
[Trait("Category", TestCategory.Unit)]
public class SectionChargePoidsMetriqueMapperTests
{
    // AC-FR19-2: the reference Fichier's PoidsMetrique section does not concern this OF - both
    // applicability fields are blank - so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_ReferenceOF_NotApplicable_ReturnsNull_AcFr19_2()
    {
        Assert.Null(SectionChargePoidsMetriqueMapper.Map(ReferenceKape22()));
    }

    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpePoidMetrique", "PM1");
            SetChamp(document, "message", "RangOpePoidMetrique", "160");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;

        L_D_SECTIONCHARGE_POIDSMETRIQUE? entity = SectionChargePoidsMetriqueMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpePoidMetrique, entity!.CodeOperation);
        Assert.Equal(source.OF, entity.OF);
        Assert.Equal(source.RangOpePoidMetrique, entity.RangOperation);
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_POIDSMETRIQUE column is explicitly sourced above - fails if a
    // future column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargePoidsMetriqueMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_POIDSMETRIQUE.CodeOperation),
            nameof(L_D_SECTIONCHARGE_POIDSMETRIQUE.OF),
            nameof(L_D_SECTIONCHARGE_POIDSMETRIQUE.RangOperation),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_POIDSMETRIQUE).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-1: SectionChargePoidsMetriqueMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargePoidsMetriqueMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargePoidsMetriqueMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
