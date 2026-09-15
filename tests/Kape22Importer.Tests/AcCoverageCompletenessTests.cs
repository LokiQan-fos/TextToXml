using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 3.6 / SM-1, the "no gap" direction. AcTraitCoverage already fails the build when a test named
// after an AC lacks its [Trait] (that mechanism is unchanged per the 2026-09-09 correction of course,
// epics.md Story 3.6 "Réancrage"); this walks the other way and fails when any individual AC-FRx-y in
// the PRD's FR-7..FR-15 range has no trait-carrying test at all in this assembly - the literal "sans
// lacune" (no gap) requirement of SM-1. Its failure form is a structural assertion, so CC-1 exempts it
// from the red-to-green ceremony, like AcTraitCoverageTests. The Epic 1 FRs (FR-1..FR-6, FR-16) and
// CTR-1..CTR-3 are covered by TextToXml.Tests's own dense trait set.
[Trait("Category", TestCategory.Unit)]
public class AcCoverageCompletenessTests
{
    // Every AC-FRx-y in the PRD for FR-7..FR-15 (PRD.md / epics.md FR Coverage Map), as (Fr, count of
    // sequential sub-ACs starting at 1). None of this range carries a lettered sub-AC.
    private static readonly Dictionary<int, int> AcCountByFr = new()
    {
        [7] = 6,
        [8] = 6,
        [9] = 6,
        [10] = 7,
        [11] = 8,
        [12] = 9,
        [13] = 5,
        [14] = 8,
        [15] = 4,
        [17] = 5,
        [18] = 4,
        [19] = 4,
    };

    // ACs this assembly cannot cover by design, not by omission - each is out of Kape22Importer's own
    // reach, so this gate excludes it rather than reporting a false gap.
    // FR9-6 is an architecture test asserting TextToXml has no notion of the derived fields it
    // describes (epics.md line 988); it lives in TextToXml.Tests (FormatIsolationTests) and is itself
    // one of CC-1's named "tests-barrière-à-la-compilation".
    // FR14-5 (WorkerAdapter<Client> status reporting) is a Launcher/Client-level concern with no
    // Kape22Importer seam to test against; it is covered in MicroServices.sln instead
    // (Launcher.Tests.WorkerRegistryTests.WorkerRegistry_RegistersGpaoImportP60AsAWorkerAdapter_AcFr14_5),
    // an assembly this gate cannot see, so it stays a documented exclusion rather than a gap.
    // FR17-1..FR17-4 (Story 4.2 mapping annex content: sourced/rule/à_clarifier status, per-OF
    // applicability rule, annex as single reference) are the annex's business content - epics.md states
    // this stays a human review, non-automatable. Only AC-FR17-5 (the mechanical completeness gate) has
    // a trait-carrying test.
    private static readonly HashSet<string> KnownExceptions =
        ["FR9-6", "FR14-5", "FR17-1", "FR17-2", "FR17-3", "FR17-4"];

    [Fact]
    [Trait("AC", "SM-1")]
    public void EveryKape22ImporterAcHasAtLeastOneTrait_Sm1()
    {
        HashSet<string> covered = [.. AcTraitValues()];

        List<string> gaps =
        [
            .. AcCountByFr
                .SelectMany(entry => Enumerable.Range(1, entry.Value).Select(n => $"FR{entry.Key}-{n}"))
                .Where(ac => !covered.Contains(ac) && !KnownExceptions.Contains(ac)),
        ];

        Assert.True(
            gaps.Count == 0,
            "No test in Kape22Importer.Tests carries an [Trait(\"AC\", ...)] for: " + string.Join(", ", gaps));
    }

    // Every [Trait("AC", "<value>")] value declared on a test method or its class in this assembly.
    private static IEnumerable<string> AcTraitValues()
    {
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (MemberInfo member in Members(type))
            {
                foreach (CustomAttributeData attribute in CustomAttributeData.GetCustomAttributes(member))
                {
                    if (attribute.AttributeType.Name == "TraitAttribute"
                        && attribute.ConstructorArguments.Count == 2
                        && (string)attribute.ConstructorArguments[0].Value! == "AC")
                    {
                        yield return (string)attribute.ConstructorArguments[1].Value!;
                    }
                }
            }
        }
    }

    private static IEnumerable<MemberInfo> Members(Type type)
    {
        yield return type;

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return method;
        }
    }
}
