using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargeChutageMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_CHUTAGE?, coded
// explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_CHUTAGE), no
// reflection, no database access - a pure function (AD-2). Written test-first (CC-1): red until
// SectionChargeChutageMapper ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The
// reference Fichier's Chutage section is applicable (CodeOpeChutage/RangOpeChutage both populated); every
// column in this table is annex-sourced, none à_clarifier.
[Trait("Category", TestCategory.Unit)]
public class SectionChargeChutageMapperTests
{
    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_SECTIONCHARGE_CHUTAGE? entity = SectionChargeChutageMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal((decimal?)source.ChutagePied, entity!.ChutagePied);
        Assert.Equal((decimal?)source.ChutageTete, entity.ChutageTete);
        Assert.Equal(source.CodeOpeChutage, entity.CodeOperation);
        Assert.Equal(source.Destination, entity.Destination);
        Assert.Equal(source.OF, entity.OF);
        Assert.Equal(source.RangOpeChutage, entity.RangOperation);
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_CHUTAGE column is explicitly sourced above - fails if a future
    // column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeChutageMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_CHUTAGE.ChutagePied),
            nameof(L_D_SECTIONCHARGE_CHUTAGE.ChutageTete),
            nameof(L_D_SECTIONCHARGE_CHUTAGE.CodeOperation),
            nameof(L_D_SECTIONCHARGE_CHUTAGE.Destination),
            nameof(L_D_SECTIONCHARGE_CHUTAGE.OF),
            nameof(L_D_SECTIONCHARGE_CHUTAGE.RangOperation),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_CHUTAGE).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-2: the annex's per-OF applicability rule - CodeOpeChutage empty means the section does not
    // concern this OF, so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpeChutage", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeChutageMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpeChutage.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpeChutage", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeChutageMapper.Map(result.Value!));
    }

    // AC-FR19-1: SectionChargeChutageMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeChutageMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargeChutageMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
