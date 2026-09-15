using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
// other facts pin MappingAnnexCompleteness.Check's five branches against small synthetic fixtures, since
// the real annex (once written correctly) only ever exercises the passing paths.
[Trait("Category", TestCategory.Unit)]
public class MappingAnnexCompletenessTests
{
    [Fact]
    [Trait("AC", "FR17-5")]
    public void MissingModelColumn_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A", "B"] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", "Champ A", MappingAnnexStatus.Sourced, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.B", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void OrphanAnnexEntry_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A"] };
        MappingAnnexEntry[] annex =
        [
            new MappingAnnexEntry("A", "Champ A", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("Zzz", "Champ Z", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("A", "Champ A", MappingAnnexStatus.Sourced, "UnknownTable"),
        ];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.Zzz", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("UnknownTable.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void InvalidStatus_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A"] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", "Champ A", "inconnu", "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal) && failure.Contains("inconnu", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithoutDeferredWorkNote_FailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A"] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "Nothing relevant here.");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithUnrelatedMentionElsewhere_StillFailsCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A"] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(
            annex,
            model,
            deferredWorkContent: "A note about T.A from an unrelated story, with no deferral marker here.");

        Assert.Contains(failures, failure => failure.Contains("T.A", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void AClarifierWithDeferredWorkNote_PassesCompleteness_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A"] };
        MappingAnnexEntry[] annex = [new MappingAnnexEntry("A", "?", MappingAnnexStatus.ToClarify, "T")];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "assumed, unverified: T.A has no known source.");

        Assert.Empty(failures);
    }

    [Fact]
    [Trait("AC", "FR17-5")]
    public void SourceeAndRegleEntries_PassWithoutDeferredWorkNote_AcFr17_5()
    {
        Dictionary<string, IReadOnlyList<string>> model = new() { ["T"] = ["A", "B"] };
        MappingAnnexEntry[] annex =
        [
            new MappingAnnexEntry("A", "Champ A", MappingAnnexStatus.Sourced, "T"),
            new MappingAnnexEntry("B", "Coulee froide si Coulee commence par '0'", MappingAnnexStatus.Rule, "T"),
        ];

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, model, deferredWorkContent: "");

        Assert.Empty(failures);
    }

    // The real AC-FR17-5 gate: reflects over the actual Story 4.1 EF model and confronts it with the real
    // annex file and the real deferred-work.md. Expected red until the annex is authored (CC-1 exemption
    // noted above) - Story 4.2 is not done until this one is green.
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

        IReadOnlyList<string> failures = MappingAnnexCompleteness.Check(annex, ModelColumnsByTable(), deferredWorkContent);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ModelColumnsByTable()
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

        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);

        Dictionary<string, IReadOnlyList<string>> result = [];
        foreach (Type clrType in downstreamEntityTypes)
        {
            IEntityType entity = context.Model.FindEntityType(clrType)!;
            result[entity.GetTableName()!] = entity.GetProperties().Select(property => property.Name).ToArray();
        }

        return result;
    }
}
