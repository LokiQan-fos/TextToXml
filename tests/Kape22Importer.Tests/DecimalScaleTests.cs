using TextToXml.Tests;
using Xunit;

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
}
