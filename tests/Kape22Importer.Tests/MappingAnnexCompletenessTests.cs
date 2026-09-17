using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 4.2 (AC-FR17-5): the annex at _bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md
// is the single source of truth mappers in Stories 4.3/4.4/4.5 may cite; this is the mechanical gate that
// keeps it honest against the Story 4.1 EF model. Same family as SchemaModelParityTests /
// Kape22ColumnLengthsParityTests and, like them, exempted from the clean red-to-green ceremony (CC-1) -
// AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5 stays red until the annex is authored. The
// other facts pin MappingAnnexCompleteness.Check's branches against small synthetic fixtures, since the
// real annex (once written correctly) only ever exercises the passing paths.
[Trait("Category", TestCategory.Unit)]
public class MappingAnnexCompletenessTests
{
    [Fact]
    [Trait("AC", "FR17-5")]
    public void MissingModelColumn_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A"), Col("B")] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "Champ A", MappingAnnexStatus.Sourced, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.B", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void OrphanAnnexEntry_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A")] };
        MappingAnnexEntry[] annex =
        [
            new MappingAnnexEntry("A", null, "Champ A", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("Zzz", null, "Champ Z", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("A", null, "Champ A", MappingAnnexStatus.Sourced, "UnknownTable"),
        ];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.Zzz", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("UnknownTable.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void InvalidStatus_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A")] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "Champ A", "inconnu", "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal) && failure.Contains("inconnu", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithoutDeferredWorkNote_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A")] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "Nothing relevant here.");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithUnrelatedMentionElsewhere_StillFailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A")] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(
            annex,
            model,
            NoKape22Fields,
            deferredWorkContent: "A note about T.A from an unrelated story, with no deferral marker here.");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithDeferredWorkNote_PassesCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A")] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "assumed, unverified: T.A has no known source.");

        Assert.Empty(failures);
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void SourceeAndRegleEntries_PassWithoutDeferredWorkNote_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [Col("A"), Col("B")] };
        MappingAnnexEntry[] annex =
        [
            new MappingAnnexEntry("A", null, "Champ A", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("B", null, "Coulee froide si Coulee commence par '0'", MappingAnnexStatus.Rule, "T"),
        ];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, NoKape22Fields, deferredWorkContent: "");

        Assert.Empty(failures);
    }

    // Story 4.2-bis (AC-FR17-1 extended): a "sourcée" row targeting a decimal EF column from an int/int?
    // KAPE22 field with no Scale is exactly the root cause behind the 20 Story 4.3/4.4 offending columns
    // (e.g. OrdreFabricationMapper writing an int straight into a narrow DECIMAL) - this is the mechanical
    // trap that must fire for a 6th mapper repeating it.
    [Fact]
    [Trait("AC", "FR17-5")]
    public void DecimalFromIntWithoutScale_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new() { ["T"] = [new ModelColumn(typeof(decimal), "A")] };
        Dictionary<string, Type> kape22Fields = new() { ["Champ"] = typeof(int?) };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", null, "KAPE22.Champ", MappingAnnexStatus.Sourced, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, kape22Fields, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal) && failure.Contains("Scale", StringComparison.Ordinal));
    }

    // Passing counterpart: Scale present makes the int-sourced column pass (AC-FR17-1 extended scenario 1),
    // and a decimal-sourced column stays exempt from the rule regardless of Scale (AC-FR17-5's 3rd bullet -
    // the rule never applies to a non-int KAPE22 source).
    [Fact]
    [Trait("AC", "FR17-5")]
    public void DecimalFromIntWithScaleAndDecimalSourcedColumn_PassCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<ModelColumn>> model = new()
        {
            ["T"] = [new ModelColumn(typeof(decimal), "A"), new ModelColumn(typeof(decimal?), "B")],
        };
        Dictionary<string, Type> kape22Fields = new() { ["ChampA"] = typeof(int?), ["ChampB"] = typeof(decimal?) };
        MappingAnnexEntry[] annex =
        [
            new MappingAnnexEntry("A", 1, "KAPE22.ChampA", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("B", null, "KAPE22.ChampB", MappingAnnexStatus.Sourced, "T"),
        ];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, kape22Fields, deferredWorkContent: "");

        Assert.Empty(failures);
    }

    // Story 4.2-bis: the widened Row regex and ParseScale run only through the real-annex fixture
    // above; this exercises them directly against raw markdown, covering Scale=0 (a legitimate value,
    // not "missing"), "-" (no Scale), and a malformed cell (fails loudly with table/column context).
    [Fact]
    [Trait("AC", "FR17-5")]
    public void Parse_ReadsScaleCell_ZeroDashAndMalformed_AcFr17_5()
    {
        const string markdown = """
            ### T

            | Colonne | Statut | Source / Règle | Scale |
            | --- | --- | --- | --- |
            | A | sourcée | KAPE22.A | 0 |
            | B | sourcée | KAPE22.B | - |

            """;

        IReadOnlyList<MappingAnnexEntry> entries = MappingAnnex.Parse(markdown);

        Assert.Equal(0, entries.Single(entry => entry.Column == "A").Scale);
        Assert.Null(entries.Single(entry => entry.Column == "B").Scale);

        const string malformed = """
            ### T

            | Colonne | Statut | Source / Règle | Scale |
            | --- | --- | --- | --- |
            | C | sourcée | KAPE22.C | abc |

            """;

        Assert.Throws<FormatException>(() => MappingAnnex.Parse(malformed));
    }

    // The real AC-FR17-5 gate: reflects over the actual Story 4.1 EF model and the actual L_D_KAPE22 field
    // types, and confronts them with the real annex file and the real deferred-work.md. Story 4.2-bis is
    // not done until this one is green with the 21 offending columns' Scale populated (see Design Notes).
    [Fact]
    [Trait("AC", "FR17-5")]
    public void AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5()
    {
        string annexPath = RepoLayout.ProjectFile(
            Path.Combine("_bmad-output", "implementation-artifacts", "annexe-mapping-dispatch-epic4.md"));
        string deferredWorkPath = RepoLayout.ProjectFile(
            Path.Combine("_bmad-output", "implementation-artifacts", "deferred-work.md"));

        IReadOnlyList<MappingAnnexEntry> annex = MappingAnnex.Parse(File.ReadAllText(annexPath));
        string deferredWorkContent = File.ReadAllText(deferredWorkPath);

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, ModelColumnsByTable(), Kape22FieldTypes(), deferredWorkContent);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyDictionary<string, Type> NoKape22Fields { get; } = new Dictionary<string, Type>();

    private static ModelColumn Col(string name) => new(typeof(string), name);

    private static IReadOnlyDictionary<string, IReadOnlyList<ModelColumn>> ModelColumnsByTable()
    {
        Type[] downstreamEntityTypes =
        [
            typeof(L_D_ORDRE_FABRICATION),
            typeof(L_D_COULEE),
            typeof(L_D_CONSIGNES),
            typeof(L_D_SECTIONCHARGE_CHUTAGE),
            typeof(L_D_SECTIONCHARGE_DECOUPE),
            typeof(L_D_SECTIONCHARGE_LINGOT),
            typeof(L_D_SECTIONCHARGE_PITS),
            typeof(L_D_SECTIONCHARGE_POIDSMETRIQUE),
            typeof(L_D_SECTIONCHARGE_REFROIDISSOIRS),
            typeof(L_D_SECTIONCHARGE_SVT),
        ];

        // Reads the real EF model (same "model-only" DbContextOptionsBuilder pattern as
        // SchemaModelParityTests.EntityType) rather than plain CLR reflection, so a future [NotMapped],
        // navigation, or shadow property is reflected in the column set MappingAnnexCompleteness checks
        // against instead of silently diverging from what EF actually maps.
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);

        Dictionary<string, IReadOnlyList<ModelColumn>> result = [];
        foreach (Type clrType in downstreamEntityTypes)
        {
            IEntityType entityType = context.Model.FindEntityType(clrType)!;
            result[clrType.Name] = entityType.GetProperties()
                .Select(property => new ModelColumn(property.ClrType, property.Name))
                .ToArray();
        }

        return result;
    }

    // Reflects L_D_KAPE22 for the CLR type of every source field the annex can cite as "KAPE22.<Field>".
    private static IReadOnlyDictionary<string, Type> Kape22FieldTypes() =>
        typeof(L_D_KAPE22).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType);
}
