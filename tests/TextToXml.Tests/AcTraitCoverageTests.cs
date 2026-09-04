using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace TextToXml.Tests;

// Meta-test pulling the Story 3.6 AC aggregator forward: a test method named after an AC / CTR / NFR
// must carry the matching [Trait], so the trait-filtered coverage view sees it and CC-1's "one green
// test per AC" stays checkable. Its failure form is a structural assertion, so CC-1 exempts it from
// the red-to-green ceremony, like the tests in FormatIsolationTests (see epics.md CC-1).
[Trait("Category", TestCategory.Unit)]
public class AcTraitCoverageTests
{
    // Trailing "_AcFr5_3", "_AcFr5_12a", "_Ctr2", "_Nfr1" on a test method name.
    private static readonly Regex SuffixPattern = new(
        @"_(?:AcFr(?<fr>\d+_\d+[a-z]?)|Ctr(?<ctr>\d+)|Nfr(?<nfr>\d+))$",
        RegexOptions.Compiled);

    [Fact]
    public void EveryAcNamedTestCarriesItsTrait()
    {
        List<string> offenders = [];
        int inspected = 0;

        foreach (MethodInfo method in TestMethods())
        {
            Match match = SuffixPattern.Match(method.Name);
            if (!match.Success)
            {
                continue;
            }

            inspected++;
            (string category, string value) = Expected(match);
            bool present = Traits(method).Any(trait => trait.Key == category && trait.Value == value);
            if (!present)
            {
                offenders.Add(
                    $"{method.DeclaringType!.Name}.{method.Name} must carry [Trait(\"{category}\", \"{value}\")].");
            }
        }

        // Guard against a silent no-op: the suffix pattern must still be matching real test methods.
        Assert.True(inspected >= 50, $"Only {inspected} AC-named test methods matched; the naming pattern may have drifted.");
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static (string Category, string Value) Expected(Match match)
    {
        if (match.Groups["fr"].Success)
        {
            return ("AC", "FR" + match.Groups["fr"].Value.Replace('_', '-'));
        }

        if (match.Groups["ctr"].Success)
        {
            return ("AC", "CTR-" + match.Groups["ctr"].Value);
        }

        return ("NFR", match.Groups["nfr"].Value);
    }

    private static IEnumerable<MethodInfo> TestMethods() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttributes()
                .Any(attribute => attribute is FactAttribute or TheoryAttribute));

    // TraitAttribute exposes its values only through its constructor arguments; read them from method
    // and declaring class both, since either can hold the [Trait].
    private static IEnumerable<KeyValuePair<string, string>> Traits(MethodInfo method)
    {
        MemberInfo[] sources = [method, method.DeclaringType!];

        return sources
            .SelectMany(CustomAttributeData.GetCustomAttributes)
            .Where(attribute => attribute.AttributeType.Name == "TraitAttribute"
                && attribute.ConstructorArguments.Count == 2)
            .Select(attribute => new KeyValuePair<string, string>(
                (string)attribute.ConstructorArguments[0].Value!,
                (string)attribute.ConstructorArguments[1].Value!));
    }
}
