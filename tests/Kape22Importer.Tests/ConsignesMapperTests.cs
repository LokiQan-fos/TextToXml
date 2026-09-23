using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.4-bis (correction to Story 4.4, FR-19): ConsignesMapper.Map(L_D_KAPE22, 7 already-mapped
// L_D_SECTIONCHARGE_* entities) -> List<L_D_CONSIGNES> now produces one "full code" row (TypeConsigne=13,
// or 24 for Decoupe's size-18 block) plus one row per positional sub-field decoded from the section's
// raw consigne code, at the offsets read directly from OrdreDeFabricationManager.cs (Code Map,
// spec-4-4-bis-decomposition-l-d-consignes-sous-champs-consignegpao.md) - a pure function (AD-2), still
// coded explicitly, no reflection, no database access. Written test-first (CC-1): red until
// ConsignesMapper ships the decomposition. Unit-only (AR-12).
[Trait("Category", TestCategory.Unit)]
public class ConsignesMapperTests
{
    // XC1 (OrdreDeFabricationManager.cs:1520-1547): full code (13) plus 5 sub-fields, against the
    // reference Fichier's real (right-trimmed, 11-character) raw code.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_ChutageSection_ProducesFullCodeAndSubFields_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_CHUTAGE chutage = SectionChargeChutageMapper.Map(source)!;
        string raw = source.CodeConsigneChutage!;
        string padded = Pad12(raw);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), chutage.CodeOperation);

        AssertRow(rows, 13, raw, 12);
        AssertRow(rows, 0, padded.Substring(0, 2), 12);
        AssertRow(rows, 1, padded.Substring(3, 1), 12);
        AssertRow(rows, 2, padded.Substring(5, 1).Trim(), 12);
        AssertRow(rows, 3, padded.Substring(7, 2).Trim(), 12);
        AssertRow(rows, 4, padded.Substring(10, 1).Trim(), 12);
        Assert.Equal(6, rows.Count);
    }

    // Boundary (code-review patch): a raw Chutage code far shorter than every one of its own offsets (2
    // characters) - confirms PadForSlicing right-pads first so no Substring throws out-of-range, and
    // every sub-field reading past the raw code's own characters comes back correctly empty once
    // .Trim()'d, instead of crashing. Every other test in this class uses a full-length or near-full-
    // length code, so this safety net was never actually exercised before.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_ChutageSection_VeryShortRawCode_PadsInsteadOfThrowing_AcFr19_3()
    {
        const string rawCode = "XY";
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeConsigneChutage", rawCode));
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_CHUTAGE chutage = SectionChargeChutageMapper.Map(source)!;
        string padded = Pad12(rawCode);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), chutage.CodeOperation);

        AssertRow(rows, 13, rawCode, 12);
        AssertRow(rows, 0, padded.Substring(0, 2), 12);
        AssertRow(rows, 1, padded.Substring(3, 1), 12);
        AssertRow(rows, 2, padded.Substring(5, 1).Trim(), 12);
        AssertRow(rows, 3, padded.Substring(7, 2).Trim(), 12);
        AssertRow(rows, 4, padded.Substring(10, 1).Trim(), 12);
        Assert.Equal(6, rows.Count);
    }

    // LA1 (OrdreDeFabricationManager.cs:1488-1514): full code (13) plus 4 sub-fields.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_LingotSection_ProducesFullCodeAndSubFields_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_LINGOT lingot = SectionChargeLingotMapper.Map(source)!;
        string raw = source.CodeConsigneLingot!;
        string padded = Pad12(raw);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), lingot.CodeOperation);

        AssertRow(rows, 13, raw, 12);
        AssertRow(rows, 15, padded.Substring(0, 3), 12);
        AssertRow(rows, 7, padded.Substring(4, 1), 12);
        AssertRow(rows, 8, padded.Substring(6, 3), 12);
        AssertRow(rows, 9, padded.Substring(10, 2), 12);
        Assert.Equal(5, rows.Count);
    }

    // PC1 (OrdreDeFabricationManager.cs:1452-1482): full code (13) plus 5 sub-fields - types 10 and 11
    // both read the identical Substring(2,3) slice (Boundaries & Constraints: legacy's exceptTypeConsigne
    // exclusion has no equivalent in the P60 dispatch path, so nothing is excluded).
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PitsSection_ProducesFullCodeAndBothTemperatureRowsFromSameSlice_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_PITS pits = SectionChargePitsMapper.Map(source)!;
        string raw = source.CodeConsignePits!;
        string padded = Pad12(raw);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), pits.CodeOperation);

        AssertRow(rows, 13, raw, 12);
        AssertRow(rows, 12, padded.Substring(0, 1), 12);
        AssertRow(rows, 10, padded.Substring(2, 3), 12);
        AssertRow(rows, 11, padded.Substring(2, 3), 12);
        AssertRow(rows, 5, padded.Substring(6, 2).Trim(), 12);
        AssertRow(rows, 6, padded.Substring(9, 3).Trim(), 12);
        Assert.Equal(rows.Single(r => r.TypeConsigne == 10).CodeConsigne, rows.Single(r => r.TypeConsigne == 11).CodeConsigne);
        Assert.Equal(6, rows.Count);
    }

    // XA1 (OrdreDeFabricationManager.cs:1642-1664): full code (13) plus 3 sub-fields.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_RefroidissoirsSection_ProducesFullCodeAndSubFields_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_REFROIDISSOIRS refroidissoirs = SectionChargeRefroidissoirsMapper.Map(source)!;
        string raw = source.CodeConsigneRefroidissoir!;
        string padded = Pad12(raw);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), refroidissoirs.CodeOperation);

        AssertRow(rows, 13, raw, 12);
        AssertRow(rows, 21, padded.Substring(0, 2).Trim(), 12);
        AssertRow(rows, 22, padded.Substring(3, 1).Trim(), 12);
        AssertRow(rows, 23, padded.Substring(5, 3).Trim(), 12);
        Assert.Equal(4, rows.Count);
    }

    // XP9 (OrdreDeFabricationManager.cs:1612-1636): not applicable in the untouched reference Fichier
    // (its own mapper already returns null), same per-OF applicability rule as every other section.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PoidsMetriqueSectionNotApplicable_ProducesNoConsigne_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        Assert.Null(SectionChargePoidsMetriqueMapper.Map(source));

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.DoesNotContain(consignes, c => c.CodeOperation == source.CodeOpePoidMetrique);
    }

    // XP9 (OrdreDeFabricationManager.cs:1612-1636): full code (13) plus 3 sub-fields, once applicable.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_PoidsMetriqueSectionApplicable_ProducesFullCodeAndSubFields_AcFr19_3()
    {
        const string rawCode = "123456789ABC";
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpePoidMetrique", "PM1");
            SetChamp(document, "message", "RangOpePoidMetrique", "160");
            SetChamp(document, "message", "CodeConsignePoidMetrique", rawCode);
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_POIDSMETRIQUE poidsMetrique = SectionChargePoidsMetriqueMapper.Map(source)!;

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), poidsMetrique.CodeOperation);

        AssertRow(rows, 13, rawCode, 12);
        AssertRow(rows, 18, "1234", 12);
        AssertRow(rows, 19, "67", 12);
        AssertRow(rows, 20, "9A", 12);
        Assert.Equal(4, rows.Count);
    }

    // The class-header comment's claim, locked in by a test (code-review patch): a section produces rows
    // iff its own mapper is applicable AND its own raw consigne code is non-blank. Here PoidsMetrique's
    // own mapper is applicable (CodeOpePoidMetrique/RangOpePoidMetrique populated) but
    // CodeConsignePoidMetrique itself is left unset/blank, so ConsignesMapper must still produce zero
    // rows for that CodeOperation.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_SectionApplicableButOwnConsigneCodeBlank_ProducesNoConsigne_AcFr19_3()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpePoidMetrique", "PM1");
            SetChamp(document, "message", "RangOpePoidMetrique", "160");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_POIDSMETRIQUE poidsMetrique = SectionChargePoidsMetriqueMapper.Map(source)!;
        Assert.True(string.IsNullOrEmpty(source.CodeConsignePoidMetrique));

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.DoesNotContain(consignes, c => c.CodeOperation == poidsMetrique.CodeOperation);
    }

    // SVT: legacy has no decode rule (OrdreDeFabricationManager.cs:1668-1669, a dead, commented-out
    // read) - unchanged single row (TypeConsigne/SizeCodeConsigne stay at their CLR default), not
    // applicable in the untouched reference Fichier.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_SvtSectionNotApplicable_ProducesNoConsigne_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        Assert.Null(SectionChargeSvtMapper.Map(source));

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.DoesNotContain(consignes, c => c.CodeOperation == source.CodeOpeSVT);
    }

    // SVT: once applicable, still exactly one unchanged row (TypeConsigne/SizeCodeConsigne at CLR
    // default), only ConsigneGPAO corrected to true.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_SvtSectionApplicable_ProducesSingleUnchangedRow_AcFr19_3()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpeSVT", "SV1");
            SetChamp(document, "message", "RangOpeSVT", "170");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_SVT svt = SectionChargeSvtMapper.Map(source)!;

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), svt.CodeOperation);

        L_D_CONSIGNES consigne = Assert.Single(rows);
        Assert.Equal(DownstreamOf.Pad(source.OF), consigne.OF);
        Assert.Equal(source.CodeConsigneSVT ?? string.Empty, consigne.CodeConsigne);
        Assert.True(consigne.ConsigneGPAO);
        Assert.Equal(0, consigne.SizeCodeConsigne);
        Assert.Equal(0, consigne.TypeConsigne);
        Assert.Null(consigne.LibelleConsigne);
    }

    // XP1 (OrdreDeFabricationManager.cs:1557-1573), size-12 block only: the reference Fichier's real raw
    // Decoupe code is naturally short (11 characters) - too short for the size-18 block's own offsets, so
    // only the size-12 rows (13/16/17) are produced (Boundaries & Constraints, I/O matrix).
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_DecoupeSection_SizeTwelveCodeOnly_ProducesOnlySizeTwelveRows_AcFr19_3()
    {
        L_D_KAPE22 source = ReferenceKape22();
        L_D_SECTIONCHARGE_DECOUPE decoupe = SectionChargeDecoupeMapper.Map(source)!;
        string raw = source.CodeConsigneDecoupe!;
        string padded = Pad12(raw);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), decoupe.CodeOperation);

        AssertRow(rows, 13, raw, 12);
        AssertRow(rows, 16, padded.Substring(0, 5).Trim(), 12);
        AssertRow(rows, 17, padded.Substring(6, 5).Trim(), 12);
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, r => r.TypeConsigne is 24 or 25 or 26 or 27 or 28 or 29);
    }

    // XP1 (OrdreDeFabricationManager.cs:1557-1599), both blocks present: a raw code long enough to also
    // carry the size-18 block's own offsets (Boundaries & Constraints: emitted "independently of the
    // size-12 block"). Real P60 data never reaches this length (deferred-work.md, story-4.4-bis); driven
    // directly through a mutated Fichier here since ConsignesMapper is a pure function of L_D_KAPE22.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_DecoupeSection_BothCodesPresent_ProducesSizeTwelveAndSizeEighteenRows_AcFr19_3()
    {
        const string rawCode = "ABCDEFGHIJKLMNOPQR";
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeConsigneDecoupe", rawCode));
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_DECOUPE decoupe = SectionChargeDecoupeMapper.Map(source)!;

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), decoupe.CodeOperation);

        AssertRow(rows, 13, rawCode, 12);
        AssertRow(rows, 16, "ABCDE", 12);
        AssertRow(rows, 17, "GHIJK", 12);
        AssertRow(rows, 24, rawCode, 18);
        AssertRow(rows, 25, "A", 18);
        AssertRow(rows, 26, "BCDEF", 18);
        AssertRow(rows, 27, "HI", 18);
        AssertRow(rows, 28, "JKLM", 18);
        AssertRow(rows, 29, "L", 18);
        Assert.Equal(9, rows.Count);
    }

    // Boundary (code-review patch): a raw Decoupe code of exactly 12 characters - one short of
    // AddDecoupe's own gate ("if (rawCode.Length < 13) return;") - must still produce only the size-12
    // rows. Existing tests only cover 11 characters (the reference fixture's natural length) and 18,
    // never this exact threshold.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_DecoupeSection_RawCodeExactlyTwelveCharacters_ProducesOnlySizeTwelveRows_AcFr19_3()
    {
        const string rawCode = "ABCDEFGHIJKL";
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeConsigneDecoupe", rawCode));
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_DECOUPE decoupe = SectionChargeDecoupeMapper.Map(source)!;
        string padded = Pad12(rawCode);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), decoupe.CodeOperation);

        AssertRow(rows, 13, rawCode, 12);
        AssertRow(rows, 16, padded.Substring(0, 5).Trim(), 12);
        AssertRow(rows, 17, padded.Substring(6, 5).Trim(), 12);
        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, r => r.TypeConsigne is 24 or 25 or 26 or 27 or 28 or 29);
    }

    // Boundary (code-review patch): a raw Decoupe code of exactly 13 characters - one character past
    // AddDecoupe's own gate - must also produce the size-18 rows, unlike the 12-character case above.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_DecoupeSection_RawCodeExactlyThirteenCharacters_ProducesSizeTwelveAndSizeEighteenRows_AcFr19_3()
    {
        const string rawCode = "ABCDEFGHIJKLM";
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
            SetChamp(document, "message", "CodeConsigneDecoupe", rawCode));
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;
        L_D_SECTIONCHARGE_DECOUPE decoupe = SectionChargeDecoupeMapper.Map(source)!;
        string padded12 = Pad12(rawCode);
        string padded18 = rawCode.PadRight(18);

        List<L_D_CONSIGNES> rows = RowsFor(MapAll(source), decoupe.CodeOperation);

        AssertRow(rows, 13, rawCode, 12);
        AssertRow(rows, 16, padded12.Substring(0, 5).Trim(), 12);
        AssertRow(rows, 17, padded12.Substring(6, 5).Trim(), 12);
        AssertRow(rows, 24, rawCode, 18);
        AssertRow(rows, 25, padded18.Substring(0, 1).Trim(), 18);
        AssertRow(rows, 26, padded18.Substring(1, 5).Trim(), 18);
        AssertRow(rows, 27, padded18.Substring(7, 2).Trim(), 18);
        AssertRow(rows, 28, padded18.Substring(9, 4).Trim(), 18);
        AssertRow(rows, 29, padded18.Substring(11, 1).Trim(), 18);
        Assert.Equal(9, rows.Count);
    }

    // AC-FR19-3: when none of the 7 sections concern this OF, ConsignesMapper produces no rows at all.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_NoSectionApplicable_ReturnsEmptyList_AcFr19_3()
    {
        List<L_D_CONSIGNES> consignes = ConsignesMapper.Map(ReferenceKape22(), null, null, null, null, null, null, null);

        Assert.Empty(consignes);
    }

    // The frozen Acceptance Criteria's first bullet, verbatim: with all 6 decodable sections applicable,
    // Map produces exactly the row set the Code Map table describes, every row ConsigneGPAO=true.
    [Fact]
    [Trait("AC", "FR19-3")]
    public void Map_AllSixDecodableSectionsApplicable_ProducesExactCodeMapRowSet_AcFr19_3()
    {
        MapResult<L_D_KAPE22> result = MapMutatedFichier(document =>
        {
            SetChamp(document, "message", "CodeOpePoidMetrique", "PM1");
            SetChamp(document, "message", "RangOpePoidMetrique", "160");
            SetChamp(document, "message", "CodeConsignePoidMetrique", "123456789ABC");
        });
        Assert.True(result.Success);
        L_D_KAPE22 source = result.Value!;

        List<L_D_CONSIGNES> consignes = MapAll(source);

        Assert.All(consignes, c => Assert.True(c.ConsigneGPAO));
        AssertTypeConsigneSet(consignes, SectionChargeChutageMapper.Map(source)!.CodeOperation, 13, 0, 1, 2, 3, 4);
        AssertTypeConsigneSet(consignes, SectionChargeLingotMapper.Map(source)!.CodeOperation, 13, 15, 7, 8, 9);
        AssertTypeConsigneSet(consignes, SectionChargePitsMapper.Map(source)!.CodeOperation, 13, 12, 10, 11, 5, 6);
        AssertTypeConsigneSet(consignes, SectionChargeRefroidissoirsMapper.Map(source)!.CodeOperation, 13, 21, 22, 23);
        AssertTypeConsigneSet(consignes, SectionChargeDecoupeMapper.Map(source)!.CodeOperation, 13, 16, 17);
        AssertTypeConsigneSet(consignes, SectionChargePoidsMetriqueMapper.Map(source)!.CodeOperation, 13, 18, 19, 20);
        Assert.Equal(6 + 5 + 6 + 4 + 3 + 4, consignes.Count);
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

    // AC-FR19-4: the mapper documents the one column it still leaves à_clarifier (the Decoupe size-18
    // block's missing independent source) with the same "assumed, unverified" marker citing
    // deferred-work.md, instead of leaving the gap silent.
    [Fact]
    [Trait("AC", "FR19-4")]
    public void ConsignesMapperSource_CitesAssumedUnverifiedForToClarifyColumns_AcFr19_4()
    {
        string source = MapperSourceText("ConsignesMapper.cs");

        Assert.Contains("assumed, unverified", source, StringComparison.Ordinal);
        Assert.Contains("deferred-work.md", source, StringComparison.Ordinal);
    }

    // AC-FR19-4 (reworded from Story 4.4): every row this mapper produces carries ConsigneGPAO=true and
    // leaves LibelleConsigne untouched (null); SizeCodeConsigne is no longer left at its CLR default for
    // any row this reference Fichier's applicable sections produce (none of them is SVT).
    [Fact]
    [Trait("AC", "FR19-4")]
    public void Map_EveryProducedConsigne_HasConsigneGpaoTrueAndSizeCodeConsigneSet_AcFr19_4()
    {
        List<L_D_CONSIGNES> consignes = MapAll(ReferenceKape22());

        Assert.NotEmpty(consignes);
        Assert.All(consignes, consigne =>
        {
            Assert.True(consigne.ConsigneGPAO);
            Assert.Null(consigne.LibelleConsigne);
            Assert.Equal(12, consigne.SizeCodeConsigne);
        });
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

    // The rows a section's own decode produced, isolated by its own CodeOperation - every row of one
    // section shares the same CodeOperation, only TypeConsigne tells them apart.
    private static List<L_D_CONSIGNES> RowsFor(List<L_D_CONSIGNES> consignes, string codeOperation) =>
        consignes.Where(c => c.CodeOperation == codeOperation).ToList();

    private static void AssertRow(List<L_D_CONSIGNES> rows, int typeConsigne, string expectedCodeConsigne, int expectedSizeCodeConsigne)
    {
        L_D_CONSIGNES row = Assert.Single(rows, r => r.TypeConsigne == typeConsigne);
        Assert.Equal(expectedCodeConsigne, row.CodeConsigne);
        Assert.Equal(expectedSizeCodeConsigne, row.SizeCodeConsigne);
        Assert.True(row.ConsigneGPAO);
    }

    private static void AssertTypeConsigneSet(List<L_D_CONSIGNES> consignes, string codeOperation, params int[] expectedTypes)
    {
        int[] actual = [.. RowsFor(consignes, codeOperation).Select(r => r.TypeConsigne).OrderBy(t => t)];
        Assert.Equal(expectedTypes.OrderBy(t => t), actual);
    }

    // Right-pads to the wire format's declared Champ width (12), mirroring ConsignesMapper.PadForSlicing -
    // a no-op once the XML-normalized value already reaches it, only ever compensating for the trailing
    // space padding the XML layer already trimmed away.
    private static string Pad12(string raw) => raw.Length >= 12 ? raw : raw.PadRight(12);

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
