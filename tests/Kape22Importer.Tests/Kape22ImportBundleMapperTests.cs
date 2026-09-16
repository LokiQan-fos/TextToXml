using System;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.5 (FR-20): Kape22ImportBundleMapper.Map composes the Story 4.3/4.4 mappers into one
// Kape22ImportBundle, then runs the 3 pure business controls FR-20 requires - no reflection, no database
// access (AD-2). Written test-first (CC-1): red until Kape22ImportBundleMapper ships. Unit-only (AR-12) -
// pure function of its two string inputs, nothing here needs a database.
[Trait("Category", TestCategory.Unit)]
public class Kape22ImportBundleMapperTests
{
    // AC-FR20-1: a valid Fichier where all 3 FR-20 controls pass. The reference Fichier's own
    // CodeConsignePits ("1 205 00 999", a real 12-char consigne code, never the literal "1") makes it
    // "hot" under AC-FR20-3's rule, and its Coulee ("165718") does not start with '0' - so the untouched
    // reference is deliberately not this test's fixture (see HotCouleeMalformed below, which uses that
    // combination on purpose). Forcing CodeConsignePits to "1" (cold) isolates the happy path from a
    // control this story adds, leaving the other two Story 4.3/4.4-derived facts (ingot sum, Pits
    // presence) exactly as the reference Fichier already has them.
    [Fact]
    [Trait("AC", "FR20-1")]
    public void Map_ValidXmlAllControlsPass_ReturnsSuccessfulBundleWithEveryEntity_AcFr20_1()
    {
        string mutatedXml = MutatedReferenceXml(document => SetChamp(document, "message", "CodeConsignePits", "1"));
        MapResult<L_D_KAPE22> plainMap = Map(mutatedXml, ReferenceFichierName);
        Assert.True(plainMap.Success, "test fixture must still map cleanly through Kape22Mapper.");

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.True(bundle.Success);
        Assert.Empty(bundle.Errors);
        Assert.Equal(plainMap.NumeroFichier, bundle.NumeroFichier);
        Assert.Equal(plainMap.OF, bundle.OF);
        Assert.Equal(plainMap.Warnings, bundle.Warnings);
        Assert.NotNull(bundle.Kape22);
        Assert.Equal(plainMap.Value!.OF, bundle.Kape22.OF);
        Assert.NotNull(bundle.OrdreFabrication);
        Assert.NotNull(bundle.Coulee);
        // The reference Fichier's PoidsMetrique/SVT sections are the natural "not applicable" fixture
        // (CodeOpePoidMetrique/RangOpePoidMetrique and CodeOpeSVT/RangOpeSVT both blank, per Story 4.4's
        // own mapper tests) - so those two stay null here even on this all-controls-pass happy path.
        Assert.NotNull(bundle.SectionChargeChutage);
        Assert.NotNull(bundle.SectionChargeDecoupe);
        Assert.NotNull(bundle.SectionChargeLingot);
        Assert.NotNull(bundle.SectionChargePits);
        Assert.Null(bundle.SectionChargePoidsMetrique);
        Assert.NotNull(bundle.SectionChargeRefroidissoirs);
        Assert.Null(bundle.SectionChargeSvt);
        Assert.NotEmpty(bundle.Consignes);
    }

    // Upstream failure: Kape22Mapper.Map itself rejects the Fichier (RequiredFieldMissing on a blanked
    // Client), so the bundle short-circuits before any Story 4.3/4.4 mapper runs - every entity stays
    // null, only the MapResult metadata rides along (AC-FR11-4 parity).
    [Fact]
    [Trait("AC", "FR20-1")]
    public void Map_UpstreamMappingFailure_ShortCircuitsWithNoEntitiesMapped_AcFr20_1()
    {
        ConversionResult conversion = Converter.Convert(BlankClientReferenceFichier(), EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, "blanking Client must not fail Step 1.");
        MapResult<L_D_KAPE22> plainMap = Map(conversion.Xml!, ReferenceFichierName);
        Assert.False(plainMap.Success, "blanking Client must fail Step 2 (RequiredFieldMissing).");

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(conversion.Xml!, ReferenceFichierName);

        Assert.False(bundle.Success);
        Assert.Equal(plainMap.Errors, bundle.Errors);
        Assert.Equal(plainMap.NumeroFichier, bundle.NumeroFichier);
        Assert.Equal(plainMap.OF, bundle.OF);
        Assert.Null(bundle.Kape22);
        Assert.Null(bundle.OrdreFabrication);
        Assert.Null(bundle.Coulee);
        Assert.Null(bundle.SectionChargeChutage);
        Assert.Null(bundle.SectionChargeDecoupe);
        Assert.Null(bundle.SectionChargeLingot);
        Assert.Null(bundle.SectionChargePits);
        Assert.Null(bundle.SectionChargePoidsMetrique);
        Assert.Null(bundle.SectionChargeRefroidissoirs);
        Assert.Null(bundle.SectionChargeSvt);
        Assert.Empty(bundle.Consignes);
    }

    // AC-FR20-2: SectionChargeRefroidissoirs.NombreLingotsFour1 + NombreLingotsFour2 must sum to the OF's
    // own NombreDemiProduit. CodeConsignePits is forced cold ("1") so only this one control is exercised.
    [Fact]
    [Trait("AC", "FR20-2")]
    public void Map_IngotFurnaceSumDivergesFromNombreDemiProduit_ReturnsDedicatedViolation_AcFr20_2()
    {
        string mutatedXml = MutatedReferenceXml(document =>
        {
            SetChamp(document, "message", "CodeConsignePits", "1");
            SetChamp(document, "message", "NombreLingotsFour1", "5");
        });

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.False(bundle.Success);
        ConversionError error = Assert.Single(bundle.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.OF!, error.Message, StringComparison.Ordinal);
        Assert.Contains("5", error.Message, StringComparison.Ordinal);
        Assert.Contains(bundle.OrdreFabrication!.NombreDemiProduit.ToString(), error.Message, StringComparison.Ordinal);

        // Entities stay populated on the bundle even though Success is false (Story 4.6 decides what a
        // failed bundle means for persistence; this mapper never withholds the mapped data).
        Assert.NotNull(bundle.OrdreFabrication);
        Assert.NotNull(bundle.SectionChargeRefroidissoirs);
    }

    // A blank NombreLingotsFour1 Champ is zero-filled onto L_D_KAPE22 (Kape22Mapper.DefaultForNonNullable,
    // production-parity behavior, not a gap this story introduces), so it drives AC-FR20-2's mismatch
    // check exactly like any other explicit value rather than needing separate "missing" handling.
    [Fact]
    [Trait("AC", "FR20-2")]
    public void Map_IngotFurnaceCountBlank_IsZeroFilledAndDrivesTheMismatchCheck_AcFr20_2()
    {
        string mutatedXml = MutatedReferenceXml(document =>
        {
            SetChamp(document, "message", "CodeConsignePits", "1");
            document.Root!.Element("message")!.Element("NombreLingotsFour1")!.Remove();
        });

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.Equal(0, bundle.SectionChargeRefroidissoirs!.NombreLingotsFour1);
        Assert.False(bundle.Success);
        ConversionError error = Assert.Single(bundle.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.OF!, error.Message, StringComparison.Ordinal);
    }

    // Accumulate-then-freeze (see Kape22ImportBundleMapper.Map): a Fichier failing two independent FR-20
    // controls at once reports both in a single pass instead of stopping at the first.
    [Fact]
    [Trait("AC", "FR20-2")]
    public void Map_TwoControlsFailSimultaneously_ReturnsBothDedicatedViolations_AcFr20_2()
    {
        string mutatedXml = MutatedReferenceXml(document =>
        {
            SetChamp(document, "message", "CodeConsignePits", "2");
            SetChamp(document, "message", "Coulee", "155718");
            SetChamp(document, "message", "NombreLingotsFour1", "5");
        });

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.False(bundle.Success);
        Assert.Equal(2, bundle.Errors.Count);
        Assert.All(bundle.Errors, error => Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code));
        Assert.Contains(bundle.Errors, error => error.Message.Contains("155718", StringComparison.Ordinal));
        Assert.Contains(bundle.Errors, error => error.Message.Contains("répartition des lingots", StringComparison.Ordinal));
    }

    // AC-FR20-3: a "hot" Coulee (CodeConsignePits != "1") whose Coulee number does not start with '0' is
    // a dedicated violation. Deliberately mutates both Champs rather than relying on the reference
    // Fichier's own coincidental values, so the test documents the rule instead of depending on fixture
    // drift.
    [Fact]
    [Trait("AC", "FR20-3")]
    public void Map_HotCouleeDoesNotStartWithZero_ReturnsDedicatedViolation_AcFr20_3()
    {
        string mutatedXml = MutatedReferenceXml(document =>
        {
            SetChamp(document, "message", "CodeConsignePits", "2");
            SetChamp(document, "message", "Coulee", "155718");
        });

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.False(bundle.Success);
        ConversionError error = Assert.Single(bundle.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.OF!, error.Message, StringComparison.Ordinal);
        Assert.Contains("155718", error.Message, StringComparison.Ordinal);
    }

    // The untouched reference Fichier happens to already be "hot" (CodeConsignePits is a real 12-char
    // consigne code, never literally "1") with a Coulee not starting with '0' - so AC-FR20-3 fires on it
    // as-is, with no other FR-20 control tripped. Kept as a second, independent confirmation alongside
    // the deliberate variant above.
    [Fact]
    [Trait("AC", "FR20-3")]
    public void Map_UnmutatedReferenceFichier_IsHotCouleeMalformedInIsolation_AcFr20_3()
    {
        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock())
            .Map(ConvertReferenceFichier(), ReferenceFichierName);

        Assert.False(bundle.Success);
        ConversionError error = Assert.Single(bundle.Errors);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("165718", error.Message, StringComparison.Ordinal);
    }

    // AC-FR20-4: an OF with no applicable L_D_SECTIONCHARGE_PITS (CodeOpePits/RangOpePits blanked) is a
    // dedicated violation. CodeConsignePits is forced cold ("1") so only this one control is exercised.
    [Fact]
    [Trait("AC", "FR20-4")]
    public void Map_NoApplicablePitsSection_ReturnsDedicatedViolation_AcFr20_4()
    {
        string mutatedXml = MutatedReferenceXml(document =>
        {
            SetChamp(document, "message", "CodeConsignePits", "1");
            SetChamp(document, "message", "CodeOpePits", "   ");
            SetChamp(document, "message", "RangOpePits", "   ");
        });

        Kape22ImportBundle bundle = new Kape22ImportBundleMapper(WinterClock()).Map(mutatedXml, ReferenceFichierName);

        Assert.False(bundle.Success);
        ConversionError error = Assert.Single(bundle.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains(bundle.OF!, error.Message, StringComparison.Ordinal);
        Assert.Null(bundle.SectionChargePits);
    }

    // AD-2: Kape22ImportBundleMapper is a pure function - no reflection, no database access. A
    // compile-barrier check (CC-1's "test-barrière-à-la-compilation" family), like the equivalent
    // Story 4.3/4.4 mapper tests.
    [Fact]
    [Trait("AC", "FR20-1")]
    public void Kape22ImportBundleMapperSource_UsesNoReflectionAndNoDatabaseAccess_AcFr20_1()
    {
        string source = MapperSourceText("Kape22ImportBundleMapper.cs");

        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
    }

    // Applies one mutation to the reference Fichier's normalized XML without mapping it, the raw text
    // Kape22ImportBundleMapper.Map itself accepts.
    private static string MutatedReferenceXml(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return document.ToString();
    }

    private static string MapperSourceText(string fileName) =>
        System.IO.File.ReadAllText(RepoLayout.ProjectFile($"src/Kape22Importer/{fileName}"));
}
