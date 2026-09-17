using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargeDecoupeMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_DECOUPE?, coded
// explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_DECOUPE), no
// reflection, no database access - a pure function (AD-2). Written test-first (CC-1): red until
// SectionChargeDecoupeMapper ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The
// reference Fichier's Decoupe section is applicable (CodeOpeDecoupe/RangOpeDecoupe both populated); every
// column in this table is annex-sourced, none à_clarifier.
[Trait("Category", TestCategory.Unit)]
public class SectionChargeDecoupeMapperTests
{
    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_SECTIONCHARGE_DECOUPE? entity = SectionChargeDecoupeMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpeDecoupe, entity!.CodeOperation);
        Assert.Equal(DecimalScale.Apply(source.LongueurMoyenne, 3), entity.LongueurMoyenne);
        Assert.Equal(source.OF, entity.OF);
        Assert.Equal(source.OutilDecoupe, entity.OutilDeDecoupe);
        Assert.Equal(source.RangOpeDecoupe, entity.RangOperation);
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_DECOUPE column is explicitly sourced above - fails if a future
    // column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeDecoupeMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_DECOUPE.CodeOperation),
            nameof(L_D_SECTIONCHARGE_DECOUPE.LongueurMoyenne),
            nameof(L_D_SECTIONCHARGE_DECOUPE.OF),
            nameof(L_D_SECTIONCHARGE_DECOUPE.OutilDeDecoupe),
            nameof(L_D_SECTIONCHARGE_DECOUPE.RangOperation),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_DECOUPE).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-2: the annex's per-OF applicability rule - CodeOpeDecoupe empty means the section does not
    // concern this OF, so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpeDecoupe", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeDecoupeMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpeDecoupe.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpeDecoupe", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeDecoupeMapper.Map(result.Value!));
    }

    // AC-FR19-1: SectionChargeDecoupeMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeDecoupeMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargeDecoupeMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
