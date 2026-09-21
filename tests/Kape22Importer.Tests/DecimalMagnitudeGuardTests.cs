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

// Story 4.10 (B-5): DecimalScale.Apply (Story 4.3-bis) corrects decimal placement but never checks a
// column's total DECIMAL(p,s) magnitude - a raw KAPE22 int whose scaled value still exceeds its target
// column's bound (DownstreamColumnMagnitudes) would otherwise overflow at INSERT time as an
// undiagnosed SQL exception. Kape22Persister's pre-SaveChanges guard catches it first, with the same
// ConversionError/REJETÉ-log shape as the missing-Coulee (AC-FR20-5) and Consignes-collision (A-5)
// checks in TransactionalPersistenceTests - but Category=Unit (AR-12, EF InMemory provider): proving
// the guard fires before any SQL statement runs needs no real SQL Server round-trip. Written
// test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public class DecimalMagnitudeGuardTests
{
    private const string InitiatingServer = "AFS017";

    // Out-of-gabarit DiametreProduit (L_D_ORDRE_FABRICATION.DiametreProduit is DECIMAL(4,1), bound
    // 1000): the raw KAPE22 int 99999, scaled by 1 (its annex Scale), becomes 9999.9 - past the
    // column's magnitude, but still a value DecimalScale.Apply itself has no reason to reject.
    [Fact]
    [Trait("AC", "4.10-B5")]
    public void Persist_OutOfGabaritScaledColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcB5()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "DiametreProduit", "99999");
        });
        Assert.True(bundle.Success, "the mutation must only trip the magnitude guard, not an upstream FR-20 control.");

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("DiametreProduit", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.OrdreFabricationRows);
        Assert.Empty(verify.CouleeRows);
        L_D_LOG_COMMANDE log = Assert.Single(verify.LogCommandeRows);
        Assert.Contains("REJETÉ", log.Message);
    }

    // The same guard, exercised through a SectionCharge* entity (DownstreamDecimalEntities' other 4
    // branches, not just OrdreFabrication): out-of-gabarit EpaisseurEnLaminage
    // (L_D_SECTIONCHARGE_LINGOT.EpaisseurEnLaminage is DECIMAL(4,1), bound 1000) - the raw KAPE22 int
    // 99999, scaled by 1, becomes 9999.9.
    [Fact]
    [Trait("AC", "4.10-B5")]
    public void Persist_OutOfGabaritSectionChargeColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcB5()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "EpaisseurEnLaminage", "99999");
        });
        Assert.True(bundle.Success, "the mutation must only trip the magnitude guard, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeLingot);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("EpaisseurEnLaminage", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.SectionChargeLingotRows);
    }

    // Boundary: 999.9 is the largest value DECIMAL(4,1) can hold (bound 1000 is exclusive,
    // Math.Abs(actual) >= bound), so the raw KAPE22 int 9999 (scaled by 1) must be accepted, not
    // rejected - pins the guard's edge against an off-by-one that would reject the legitimate maximum.
    [Fact]
    [Trait("AC", "4.10-B5")]
    public void Persist_ScaledColumnExactlyAtGabarit_Succeeds_AcB5()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "DiametreProduit", "9999");
        });
        Assert.True(bundle.Success, "the mutation must only affect DiametreProduit's magnitude, not an upstream FR-20 control.");

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(error => error.Message)));

        using AscoLsiDbContext verify = contexts.Reader();
        L_D_ORDRE_FABRICATION inserted = Assert.Single(verify.OrdreFabricationRows);
        Assert.Equal(999.9m, inserted.DiametreProduit);
    }

    // C-1 (Épic 4 retro #3): the same guard, exercised through the SectionChargeChutage branch - not
    // previously covered - out-of-gabarit ChutageTete (L_D_SECTIONCHARGE_CHUTAGE.ChutageTete is
    // DECIMAL(3,2), bound 10) - the raw KAPE22 int 99999, scaled by 2, becomes 999.99. ChutagePied shares
    // the same DECIMAL(3,2)/bound-10 shape in the same branch (the reference Fichier leaves it blank, so
    // ChutageTete is the one this fixture can mutate), so this one property proves the branch.
    [Fact]
    [Trait("AC", "4.11-C1")]
    public void Persist_OutOfGabaritSectionChargeChutageColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcC1()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "ChutageTete", "99999");
        });
        Assert.True(bundle.Success, "the mutation must only trip the magnitude guard, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeChutage);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("ChutageTete", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.SectionChargeChutageRows);
    }

    // C-1: the same guard, exercised through the SectionChargeDecoupe branch - out-of-gabarit
    // LongueurMoyenne (L_D_SECTIONCHARGE_DECOUPE.LongueurMoyenne is DECIMAL(5,3), bound 100) - the raw
    // KAPE22 int 999999, scaled by 3, becomes 999.999.
    [Fact]
    [Trait("AC", "4.11-C1")]
    public void Persist_OutOfGabaritSectionChargeDecoupeColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcC1()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "LongueurMoyenne", "999999");
        });
        Assert.True(bundle.Success, "the mutation must only trip the magnitude guard, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargeDecoupe);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("LongueurMoyenne", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.SectionChargeDecoupeRows);
    }

    // C-1: the same guard, exercised through the SectionChargePits branch - out-of-gabarit H2Coulee
    // (L_D_SECTIONCHARGE_PITS.H2Coulee is DECIMAL(3,1), bound 10) - the raw KAPE22 int 99999, scaled by
    // 1, becomes 9999.9.
    [Fact]
    [Trait("AC", "4.11-C1")]
    public void Persist_OutOfGabaritSectionChargePitsColumn_RejectsWithBusinessRuleViolationAndNoInserts_AcC1()
    {
        InMemoryContextFactory contexts = new();
        Kape22ImportBundle bundle = MapMutatedBundle(d =>
        {
            SetChamp(d, "message", "Coulee", "065718");
            SetChamp(d, "message", "H2Coulee", "99999");
        });
        Assert.True(bundle.Success, "the mutation must only trip the magnitude guard, not an upstream FR-20 control.");
        Assert.NotNull(bundle.SectionChargePits);

        using AscoLsiDbContext context = contexts.Next();
        ImportResult result = new Kape22Persister(context, Configuration(), WinterClock()).Persist(bundle);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.BusinessRuleViolation, error.Code);
        Assert.Contains("H2Coulee", error.Message, StringComparison.Ordinal);

        using AscoLsiDbContext verify = contexts.Reader();
        Assert.Empty(verify.Kape22Rows);
        Assert.Empty(verify.SectionChargePitsRows);
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
