using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.3-bis: DecimalScale.Apply's own arithmetic, exercised directly against literal values - the
// mapper tests (OrdreFabricationMapperTests, SectionCharge*MapperTests) confirm each mapper wires the
// right raw KAPE22 field and annex scale into DecimalScale.Apply; this confirms Apply's conversion itself
// matches the spec's I/O & Edge-Case Matrix (spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md).
[Trait("Category", TestCategory.Unit)]
public class DecimalScaleTests
{
    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_InRangeRawInt_DividesByTenToThePowerOfScale()
    {
        Assert.Equal(1.8m, DecimalScale.Apply(18, 1));
    }

    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_ScaleZero_ReturnsRawValueUnchanged()
    {
        Assert.Equal(5m, DecimalScale.Apply(5, 0));
    }

    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_NullableOverloadWithNullRawValue_ReturnsNull()
    {
        Assert.Null(DecimalScale.Apply((int?)null, 3));
    }

    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_ZeroRawValue_ReturnsZero()
    {
        Assert.Equal(0m, DecimalScale.Apply(0, 1));
    }

    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_ScaleOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => DecimalScale.Apply(18, 4));
        Assert.Equal("scale", exception.ParamName);
    }

    // Scale 2 (ChutageTete/ChutagePied's annex scale). Both are 0 on the reference fixture (P60_847_682_001),
    // so this sources its known raw value from P60_847_682_002 instead, the way
    // Kape22ProductionDataParityTests maps a named P60/ fixture directly.
    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_ScaleTwo_DividesByOneHundred()
    {
        L_D_KAPE22 source = MappedFichier("P60_847_682_002");

        Assert.Equal(1m, DecimalScale.Apply(source.ChutageTete, 2));
        Assert.Equal(0.02m, DecimalScale.Apply(source.ChutagePied, 2));
    }

    // Scale 3 (LongueurCD's annex scale). The reference fixture's own known raw value (11220) is
    // non-zero, so no other fixture is needed here.
    [Fact]
    [Trait("AC", "FR18-1")]
    public void Apply_ScaleThree_DividesByOneThousand()
    {
        L_D_KAPE22 source = ReferenceKape22();

        Assert.Equal(11.22m, DecimalScale.Apply(source.LongueurCD, 3));
    }

    // Maps a named P60/ fixture (not just the reference one) the same way
    // Kape22ProductionDataParityTests.MappedFichier_MatchesLegacyProductionRow does: raw bytes ->
    // normalized XML -> mapped entity.
    private static L_D_KAPE22 MappedFichier(string fichierName)
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture(fichierName), EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, $"{fichierName} failed to convert.");
        MapResult<L_D_KAPE22> result = Map(conversion.Xml!, fichierName);
        Assert.True(result.Success, $"{fichierName} failed to map.");
        return result.Value!;
    }
}
