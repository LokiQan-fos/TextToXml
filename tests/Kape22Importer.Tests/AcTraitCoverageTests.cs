using System;
using System.Collections.Generic;
using System.Reflection;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Kape22Importer.Tests counterpart of TextToXml.Tests.AcTraitCoverageTests (shared scan logic in
// AcTraitCoverage, linked in via Kape22Importer.Tests.csproj): the AC->[Trait] gate previously only
// ran over TextToXml.Tests, leaving Epic 2/3 AC-named tests (e.g. P60XsdTests) unchecked.
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
