using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4 (FR-19): ConsignesMapper.Map(L_D_KAPE22, 7 already-mapped L_D_SECTIONCHARGE_* entities) ->
// List<L_D_CONSIGNES>, coded explicitly from the Story 4.2 annex (annexe-mapping-dispatch-epic4.md §
// L_D_CONSIGNES), no reflection, no database access - a pure function (AD-2). Written test-first (CC-1):
// red until ConsignesMapper ships. Unit-only (AR-12) - epics.md marks CC-6/CC-7 not applicable. The
// reference Fichier's PoidsMetrique and SVT sections are not applicable (Story 4.2 annex's per-OF rule),
// so their SectionCharge*Mapper.Map already returns null - one test per of the 7 owning sections covers
// both the applicable and non-applicable shape.
[Trait("Category", TestCategory.Unit)]
public class ConsignesMapperTests
{
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_ChutageSection_ProducesConsigneWithChutageFk_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_CHUTAGE chutage = SectionChargeChutageMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == chutage.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneChutage, consigne.CodeConsigne);
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_DecoupeSection_ProducesConsigneWithDecoupeFk_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_DECOUPE decoupe = SectionChargeDecoupeMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == decoupe.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneDecoupe, consigne.CodeConsigne);
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_LingotSection_ProducesConsigneWithLingotFk_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_LINGOT lingot = SectionChargeLingotMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == lingot.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneLingot, consigne.CodeConsigne);
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PitsSection_ProducesConsigneWithPitsFk_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_PITS pits = SectionChargePitsMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == pits.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsignePits, consigne.CodeConsigne);
    }

    // The reference Fichier's PoidsMetrique section is not applicable (annex per-OF rule): its own
    // mapper already returns null, so ConsignesMapper produces no row for it either.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PoidsMetriqueSectionNotApplicable_ProducesNoConsigne_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        Assert.Null(SectionChargePoidsMetriqueMapper.Map(source));

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.DoesNotContain(consignes, c => c.CodeOperation == source.CodeOpePoidMetrique);
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PoidsMetriqueSectionApplicable_ProducesConsigneWithPoidsMetriqueFk_AcFr19_3()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpePoidMetrique", "PM1");
            SetChamp(document, "message", "RangOpePoidMetrique", "160");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_POIDSMETRIQUE poidsMetrique = SectionChargePoidsMetriqueMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == poidsMetrique.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsignePoidMetrique, consigne.CodeConsigne);
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_RefroidissoirsSection_ProducesConsigneWithRefroidissoirsFk_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_REFROIDISSOIRS refroidissoirs = SectionChargeRefroidissoirsMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == refroidissoirs.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneRefroidissoir, consigne.CodeConsigne);
    }

    // The reference Fichier's SVT section is not applicable (annex per-OF rule): its own mapper already
    // returns null, so ConsignesMapper produces no row for it either.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_SvtSectionNotApplicable_ProducesNoConsigne_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        Assert.Null(SectionChargeSvtMapper.Map(source));

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.DoesNotContain(consignes, c => c.CodeOperation == source.CodeOpeSVT);
    }

    // AC-FR19-3 / AC-FR19-4: ConsigneGPAO is always false (the real row, not the legacy GPAO mirror);
    // LibelleConsigne/SizeCodeConsigne/TypeConsigne are à_clarifier and stay at their CLR default.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void Map_EveryProducedConsigne_LeavesToClarifyColumnsAtClrDefault_AcFr19_4()
    {
        List<L_D_CONSIGNES> consignes = MapAll(ReferenceKape22());

        Assert.NotEmpty(consignes);
        Assert.All(consignes, consigne =>
        {
            Assert.False(consigne.ConsigneGPAO);
            Assert.Null(consigne.LibelleConsigne);
            Assert.Equal(0, consigne.SizeCodeConsigne);
            Assert.Equal(0, consigne.TypeConsigne);
        });
    }

    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_SvtSectionApplicable_ProducesConsigneWithSvtFk_AcFr19_3()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpeSVT", "SV1");
            SetChamp(document, "message", "RangOpeSVT", "170");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_SVT svt = SectionChargeSvtMapper.Map(source)!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        L_D_CONSIGNES consigne = Assert.Single(consignes, c => c.CodeOperation == svt.CodeOperation);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneSVT, consigne.CodeConsigne);
    }

    // AC-FR19-3: when none of the 7 sections concern this OF, ConsignesMapper produces no rows at all.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_NoSectionApplicable_ReturnsEmptyList_AcFr19_3()
    {
        List<L_D_CONSIGNES> consignes = ConsignesMapper.Map(ReferenceKape22(), null, null, null, null, null, null, null);

        Assert.Empty(consignes);
    }

    // AC-FR19-1: ConsignesMapper is a pure function - no reflection, no database access.
    [Fact]
    [Trait("AC", "FR19-1")]
    public void ConsignesMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr19_1()
    {
        string source = MapperSourceText("ConsignesMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // AC-FR19-4: the mapper documents the à_clarifier columns it leaves untouched with the same
    // "assumed, unverified" marker citing deferred-work.md, instead of leaving the gap silent.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void ConsignesMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr19_4()
    {
        string source = MapperSourceText("ConsignesMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    // Composes ConsignesMapper.Map with the 7 SectionCharge*Mapper results, the same wiring the future
    // Story 4.5 orchestrator will use.
    private static List<L_D_CONSIGNES> MapAll(L_D_KAPE22 source) => ConsignesMapper.Map(
        source,
        SectionChargeChutageMapper.Map(source),
        SectionChargeDecoupeMapper.Map(source),
        SectionChargeLingotMapper.Map(source),
        SectionChargePitsMapper.Map(source),
        SectionChargePoidsMetriqueMapper.Map(source),
        SectionChargeRefroidissoirsMapper.Map(source),
        SectionChargeSvtMapper.Map(source));

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
