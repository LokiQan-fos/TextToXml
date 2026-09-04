using System;
using System.Collections.Generic;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// AC-FR7-3: every Kape22FileMessage property must be either mapped by Kape22Mapper (default
// case-insensitive name or the Annexe B naming exception) or explicitly ignored, so a new Detail
// Champ that lands in neither breaks this test build instead of silently dropping data. Compilation-
// barrier style (CC-1's own exemption clause): there is no red state to author here, since the
// assertion only needs Kape22FileMessage and L_D_KAPE22 to exist to run at all.
[Trait("Category", TestCategory.Unit)]
public class Kape22MapperCompletenessTests
{
    [Fact]
    [Trait("AC", "FR7-3")]
    public void EveryKape22FileMessageProperty_IsMappedOrIgnored_AcFr7_3()
    {
        List<string> offenders = [];

        foreach (PropertyInfo source in typeof(Kape22FileMessage).GetProperties())
        {
            if (Kape22Mapper.IsIgnored(source.Name))
            {
                continue;
            }

            string targetName = Kape22Mapper.ResolveTargetName(source.Name);

            bool mapped = typeof(L_D_KAPE22).GetProperty(
                targetName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null;

            if (!mapped)
            {
                offenders.Add(
                    $"Kape22FileMessage.{source.Name}: no L_D_KAPE22 property named '{targetName}' " +
                    "(case-insensitive), and it is not in Kape22Mapper's ignored set.");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
