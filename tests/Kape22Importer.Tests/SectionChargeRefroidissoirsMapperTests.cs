using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): SectionChargeRefroidissoirsMapper.Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_REFROIDISSOIRS?,
// coded explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md §
// L_D_SECTIONCHARGE_REFROIDISSOIRS), no reflection, no database access - a pure function (AD-2). Written
// test-first (CC-1): red until SectionChargeRefroidissoirsMapper ships. Unit-only (AR-12) - epics.md
// marks CC-6/CC-7 not applicable. The reference Fichier's Refroidissoirs section is applicable
// (CodeOpeRefroidissoir/RangOpeRefroidissoir both populated). OFInterne is à_clarifier - a Story 4.4
// annex-drift finding (deferred-work.md), not a homonymous KAPE22 field.
[Trait("Category", TestCategory.Unit)]
public class SectionChargeRefroidissoirsMapperTests
{
    [Fact]
    [Trait("AC", "FR19-1")]
    public void Map_ApplicableOF_MapsEverySourcedColumn_AcFr19_1()
    {
        L_D_KAPE22 source = ReferenceKape22();

        L_D_SECTIONCHARGE_REFROIDISSOIRS? entity = SectionChargeRefroidissoirsMapper.Map(source);

        Assert.NotNull(entity);
        Assert.Equal(source.CodeOpeRefroidissoir, entity!.CodeOperation);
        Assert.Equal(source.GazScarfing, entity.GazScarfing);
        Assert.Equal(source.LongueurScarfingPied, entity.LongueurScarfingPied);
        Assert.Equal(source.LongueurScarfingTete, entity.LongueurScarfingTete);
        Assert.Equal(source.MatriculeClient, entity.MatriculeClient);
        Assert.Equal(source.MiseAuMille, entity.MiseAuMille);
        Assert.Equal(source.NombreLingotsFour1, entity.NombreLingotsFour1);
        Assert.Equal(source.NombreLingotsFour2, entity.NombreLingotsFour2);
        Assert.Equal(source.NuanceMarquage, entity.NuanceMarquage);
        Assert.Equal(source.OF, entity.OF);
        Assert.Equal(source.OFDestination, entity.OFDestination);
        Assert.Equal(source.OFOrigin, entity.OFOrigin);
        Assert.Equal(source.OxygeneInferieur, entity.OxygeneInferieur);
        Assert.Equal(source.OxygeneLatent, entity.OxygeneLatent);
        Assert.Equal(source.OxygeneSuperieur, entity.OxygeneSuperieur);
        Assert.Equal(source.RangOpeRefroidissoir, entity.RangOperation);
        Assert.Equal(source.RefroidissementBloom, entity.RefroidissementBloom);
        Assert.Equal(source.VitesseV1, entity.VitesseV1);
        Assert.Equal(source.VitesseV2, entity.VitesseV2);
        Assert.Equal(source.VitesseV3, entity.VitesseV3);
    }

    // AC-FR19-4: OFInterne stays at its CLR default (null) - the annex-drift finding this story made
    // (no unambiguous L_D_KAPE22 field), see deferred-work.md.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void Map_ToClarifyColumn_StaysAtClrDefault_AcFr19_4()
    {
        L_D_SECTIONCHARGE_REFROIDISSOIRS entity = SectionChargeRefroidissoirsMapper.Map(ReferenceKape22())!;

        Assert.Null(entity.OFInterne);
    }

    // AC-FR19-1: every L_D_SECTIONCHARGE_REFROIDISSOIRS column is either explicitly sourced above or the
    // one à_clarifier column - fails if a future column is added without being accounted for.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeRefroidissoirsMapper_EveryColumnIsAccountedForByAKnownList_AcFr19_1()
    {
        string[] documented =
        [
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.CodeOperation),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.GazScarfing),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.LongueurScarfingPied),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.LongueurScarfingTete),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.MatriculeClient),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.MiseAuMille),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.NombreLingotsFour1),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.NombreLingotsFour2),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.NuanceMarquage),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OF),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OFDestination),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OFInterne),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OFOrigin),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OxygeneInferieur),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OxygeneLatent),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.OxygeneSuperieur),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.RangOperation),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.RefroidissementBloom),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.VitesseV1),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.VitesseV2),
            nameof(L_D_SECTIONCHARGE_REFROIDISSOIRS.VitesseV3),
        ];

        List<string> undocumented = typeof(L_D_SECTIONCHARGE_REFROIDISSOIRS).GetProperties()
            .Select(property => property.Name)
            .Where(name => !documented.Contains(name))
            .ToList();

        Assert.True(undocumented.Count == 0, "Uncovered column(s): " + string.Join(", ", undocumented));
    }

    // AC-FR19-2: the annex's per-OF applicability rule - CodeOpeRefroidissoir empty means the section
    // does not concern this OF, so the mapper returns null rather than a default row.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_CodeOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeOpeRefroidissoir", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeRefroidissoirsMapper.Map(result.Value!));
    }

    // AC-FR19-2: same rule on the other applicability field, RangOpeRefroidissoir.
    [Fact]
    [Trait("AC", "FR19-2")]
    public void Map_RangOperationEmpty_ReturnsNull_AcFr19_2()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "RangOpeRefroidissoir", string.Empty));
        Assert.True(result.Success);

        Assert.Null(SectionChargeRefroidissoirsMapper.Map(result.Value!));
    }

    // AC-FR19-1: SectionChargeRefroidissoirsMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void SectionChargeRefroidissoirsMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("SectionChargeRefroidissoirsMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR19-4: the mapper documents the à_clarifier column it leaves untouched with the same
    // "assumed, unverified" marker citing deferred-work.md, instead of leaving the gap silent.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void SectionChargeRefroidissoirsMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr19_4()
    {
        string source = MapperSourceText("SectionChargeRefroidissoirsMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
