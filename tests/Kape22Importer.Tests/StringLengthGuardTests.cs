using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// C-4 (Épic 4 retro #3): DownstreamColumnLengths.MaxLengths already bounds every Story 4.1 downstream
// table's string columns for the EF model (AscoLsiDbContext.ApplyDownstreamColumnLengths), but nothing
// checked a mapped value against it before SaveChanges - an over-long string would otherwise overflow at
// INSERT time as an undiagnosed SQL truncation error, the same gap B-5 already closed for decimal
// magnitude. Kape22Persister's pre-SaveChanges guard catches it first, with the same
// ConversionError/REJETÉ-log shape as the Consignes-collision (A-5) and magnitude-overflow (B-5) checks.
// Category=Unit (AR-12, EF InMemory provider): proving the guard fires before any SQL statement runs
// needs no real SQL Server round-trip. Written test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public class StringLengthGuardTests
{
    private const string InitiatingServer = "AFS017";

    // Out-of-bound MarqueCommerciale (L_D_ORDRE_FABRICATION.MarqueCommerciale is bounded to 9
    // characters) - a 20-character raw KAPE22 value, copied verbatim by OrdreFabricationMapper.
    [Fact]
    [Trait("AC", "4.11-C4")]
    public void Persist_OverLongOrdreFabricationColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcC4()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "MarqueCommerciale", new string('A', 20));
        });
        Assert.True(bundle.Success, "the mutation must only trip the length guard, not an upstream FR-20 control.");

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("MarqueCommerciale", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.OrdreFabricationRows);
    }

    // The same guard, exercised through a Consignes row (a list entity, unlike FindMagnitudeOverflow's
    // single-instance entities) - out-of-bound CodeConsigne (L_D_CONSIGNES.CodeConsigne is bounded to 18
    // characters), sourced from the raw KAPE22 CodeConsignePits field.
    [Fact]
    [Trait("AC", "4.11-C4")]
    public void Persist_OverLongConsignesColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcC4()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "CodeConsignePits", new string('9', 30));
        });
        Assert.True(bundle.Success, "the mutation must only trip the length guard, not an upstream FR-20 control.");
        Assert.NotEmpty(bundle.Consignes);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("CodeConsigne", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.ConsignesRows);
    }

    // Boundary: a value at exactly its column's bound must be accepted, not rejected - pins the guard's
    // edge against an off-by-one that would reject a legitimate maximum-length value.
    [Fact]
    [Trait("AC", "4.11-C4")]
    public void Persist_StringColumnExactlyAtItsBound_Succeeds_AcC4()
    {
        InMemoryContextFactory contexts = new();
        string atBound = new('A', 9);
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "MarqueCommerciale", atBound);
        });
        Assert.True(bundle.Success, "the mutation must only affect MarqueCommerciale's length, not an upstream FR-20 control.");

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));

        using AscoLsiDbContext verify = contexts.Reader();
        L_D_ORDRE_FABRICATION inserted = Assert.Single(verify.OrdreFabricationRows);
        Assert.Equal(atBound, inserted.MarqueCommerciale);
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
