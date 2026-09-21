using System;
using System.Collections.Generic;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// C-2 (Épic 4 retro #3): the A-5 pre-check's GroupBy key used plain tuple equality, so two Consignes
// rows sharing the same CodeOperation up to case only (for example "XC1" and "xc1") were treated as two
// distinct groups and sailed through to ConsignesRows.AddRange - only the real SQL Server's
// case-insensitive collation would ever have caught the natural-key collision, and only as an
// undiagnosed SQL failure, never the A-5 REJETÉ shape. Category=Unit (AR-12, EF InMemory provider): the
// GroupBy pre-check is pure in-memory LINQ over bundle.Consignes, so no real SQL Server round-trip is
// needed to prove it fires before AddRange. Written test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public class ConsignesCaseInsensitiveCollisionTests
{
    private const string InitiatingServer = "AFS017";

    [Fact]
    [Trait("AC", "4.11-C2")]
    public void Persist_ConsignesCodeOperationCollidesOnlyByCase_RejectsWithOriginalCasingInMessage_AcC2()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            string codeOpeChutage = d.Root!.Element("message")!.Element("CodeOpeChutage")!.Value;
            SetChamp(d, "message", "CodeOpeDecoupe", codeOpeChutage.ToLowerInvariant());
        });
        Assert.True(bundle.Success, "the case-only mutation must not trip an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeChutage);
        Assert.NotNull(bundle.SectionChargeDecoupe);
        Assert.NotEqual(bundle.SectionChargeChutage!.CodeOperation, bundle.SectionChargeDecoupe!.CodeOperation, StringComparer.Ordinal);
        Assert.Equal(bundle.SectionChargeChutage!.CodeOperation, bundle.SectionChargeDecoupe!.CodeOperation, StringComparer.OrdinalIgnoreCase);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);

        // The message must carry the group's original-case CodeOperation (the Chutage row, added first
        // by ConsignesMapper.Map), never an uppercased/normalized form.
        Assert.Contains(bundle.SectionChargeChutage!.CodeOperation, error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.ConsignesRows);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
                ["Import:InitiatingServer"] = InitiatingServer,
            })
            .Build();
}
