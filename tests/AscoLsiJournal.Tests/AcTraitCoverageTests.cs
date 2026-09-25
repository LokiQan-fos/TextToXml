using System;
using System.Collections.Generic;
using System.Reflection;
using TextToXml.Tests;

namespace AscoLsiJournal.Tests;

// AscoLsiJournal.Tests counterpart of TextToXml.Tests.AcTraitCoverageTests (shared scan logic in
// AcTraitCoverage, linked in via AscoLsiJournal.Tests.csproj).
[Trait("Category", TestCategory.Unit)]
public class AcTraitCoverageTests
{
    [Fact]
    public void EveryAcNamedTestCarriesItsTrait()
    {
        (List<string> offenders, int inspected) = AcTraitCoverage.FindOffenders(Assembly.GetExecutingAssembly());

        // Guard against a silent no-op: the suffix pattern must still be matching real test methods.
        Assert.True(inspected >= 9, $"Only {inspected} AC-named test methods matched; the naming pattern may have drifted.");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
