using System;
using System.Collections.Generic;
using System.Reflection;
using TextToXml.Tests;

namespace P89Converter.Tests;

// P89Converter.Tests counterpart of TextToXml.Tests.AcTraitCoverageTests (shared scan logic in
// AcTraitCoverage, linked in via P89Converter.Tests.csproj).
[Trait("Category", TestCategory.Unit)]
public class AcTraitCoverageTests
{
    [Fact]
    public void EveryAcNamedTestCarriesItsTrait()
    {
        (List<string> offenders, int inspected) = AcTraitCoverage.FindOffenders(Assembly.GetExecutingAssembly());

        // Guard against a silent no-op: the suffix pattern must still be matching real test methods.
        Assert.True(inspected >= 8, $"Only {inspected} AC-named test methods matched; the naming pattern may have drifted.");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
