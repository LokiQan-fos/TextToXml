using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 4.12 (AC-FR19-5): LibelleConsigneResolver, the pure port of the legacy
// LibelleConsigneController.GetLibelle, against a hand-built ConsigneReferenceData snapshot. One test per
// row of the spec's I/O & Edge-Case Matrix plus one per legacy branch. Unit-only (AR-12): no database.
[Trait("Category", TestCategory.Unit)]
public class LibelleConsigneResolverTests
{
    private static readonly DateTime Older = new(2020, 1, 1);

    private static readonly DateTime Newer = new(2024, 6, 1);

    private static readonly ConsigneReferenceData Snapshot = new()
    {
        Chutage = [Section("XC1", 3, "5", "Chute standard")],
        CodeOutilCoupe = [Section("XC1", 2, "7", "Scie 7")],
        Decoupe = [Section("XP1", 30, "A", "Découpe A")],
        DegazageDetail =
        [
            new DegazageDetailReference { Code = 1, H21 = 2.50m, H22 = 3.00m, H23 = 4.00m, H24 = 5.50m, Id = 1, ProfilProduit = "RD", SectionMax = 200.0m, SectionMin = 100.0m },
        ],
        DegazageGlobal = [1],
        Lingot = [Section("LA1", 7, "0", "Pas de scarfing"), Section("LA1", 8, "X", "Premier"), Section("LA1", 8, "x", "Second")],
        Marquage =
        [
            new MarquageReference { CodeConsigne = "M", Consignes = 4, DateMaj = Older, LibellePied = "P", LibelleSection = "S", LibelleTete = "T", Section = "XC1" },
        ],
        Pits = [Section("PC1", 6, "012", "Préchauffage normal", Older), Section("PC1", 5, "1", "  ")],
        PoidsMetrique = [Section("XP9", 21, "B", "Poids B")],
        PrechauffageParticulier = [new PrechauffageParticulierReference { Code = 3, DateMaj = Newer, Libelle = "Particulier 3" }],
        Refroidissement = [new CodeConsigneReference { Code = "00", DateMaj = Older, Libelle = "Refroidissement à l'air" }],
        Refroidissoirs = [Section("FD1", 9, "2", "Refroidissoir 2")],
        Smq = [new CodeConsigneReference { Code = "   ", Libelle = "Pas de consigne" }],
    };

    // Matrix row "Computed type": no reference row is consulted, so no DateMaj is reported either.
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("PC1", 10, "205", "1250")]
    [InlineData("PC1", 11, "205", "1200")]
    [InlineData("LA1", 15, "107", "1070")]
    [InlineData("LA9", 15, "10 7", "100")]
    [InlineData("XC1", 0, "00", "0,0")]
    [InlineData("XC1", 0, "C4", "3,4")]
    [InlineData("XC1", 1, "B", "2")]
    [InlineData("XP1", 16, "11400", "11,400")]
    [InlineData("XP1", 17, "5", "0,005")]
    [InlineData("XP1", 26, "250", "0,250")]
    [InlineData("XP1", 27, "BC", "BC")]
    [InlineData("XP9", 18, "2500", "2,500")]
    [InlineData("XP9", 19, "ABC", "ABC")]
    [InlineData("XP9", 20, "42", "42")]
    public void Resolve_ComputedType_FormatsLikeLegacy_AcFr19_5(string section, int type, string code, string expected)
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve(section, type, code, Snapshot);

        Assert.Equal(expected, resolved.Libelle);
        Assert.Null(resolved.LatestDateMaj);
    }

    // Matrix row "Lookup hit", plus one lookup per remaining section table.
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("LA1", 7, "0", "Pas de scarfing")]
    [InlineData("XA1", 21, "00", "Refroidissement à l'air")]
    [InlineData("XC1", 4, "M", "section: S - Pied: P - Tête: T")]
    [InlineData("XC1", 2, "7", "Scie 7")]
    [InlineData("XC1", 3, "5", "Chute standard")]
    [InlineData("XP1", 30, "A", "Découpe A")]
    [InlineData("XP9", 21, "B", "Poids B")]
    [InlineData("FD1", 9, "2", "Refroidissoir 2")]
    public void Resolve_LookupHit_ReturnsReferenceLibelle_AcFr19_5(string section, int type, string code, string expected)
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve(section, type, code, Snapshot);

        Assert.Equal(expected, resolved.Libelle);
        Assert.Equal(Older, resolved.LatestDateMaj);
    }

    // Matrix row "nchar padding": the SMQ Code is nchar(3), stored as 3 spaces, and still matches "".
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_PaddedNcharCode_MatchesIgnoringTrailingSpaces_AcFr19_5()
    {
        Assert.Equal("Pas de consigne", LibelleConsigneResolver.Resolve("XA1", 23, string.Empty, Snapshot).Libelle);
    }

    // Matrix row "No rule / no match", plus an unknown section.
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("PC1", 13, "012345678901")]
    [InlineData("XP1", 24, "012345678901234567")]
    [InlineData("XP1", 30, "Z")]
    [InlineData("XA1", 21, "99")]
    [InlineData("SVT", 0, "1")]
    public void Resolve_NoRuleOrNoMatch_ReturnsQuestionMark_AcFr19_5(string section, int type, string code)
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve(section, type, code, Snapshot);

        Assert.Equal("?", resolved.Libelle);
        Assert.Null(resolved.LatestDateMaj);
    }

    // Matrix row "Unparsable input": the legacy catch turns every exception into "?".
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("PC1", 10, "AB5")]
    [InlineData("PC1", 11, "1")]
    [InlineData("PC1", 6, "")]
    [InlineData("PC1", 6, "X12")]
    [InlineData("LA1", 15, "10")]
    [InlineData("XC1", 0, "AX")]
    [InlineData("XP1", 16, "12A")]
    [InlineData("XA1", 22, "A")]
    public void Resolve_UnparsableCode_ReturnsQuestionMark_AcFr19_5(string section, int type, string code)
    {
        Assert.Equal("?", LibelleConsigneResolver.Resolve(section, type, code, Snapshot).Libelle);
    }

    // Legacy regexes are unanchored: a type 0/1 code with no match anywhere gives "?".
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData(0, "1")]
    [InlineData(1, "12")]
    public void Resolve_ChutageCodeWithoutRegexMatch_ReturnsQuestionMark_AcFr19_5(int type, string code)
    {
        Assert.Equal("?", LibelleConsigneResolver.Resolve("XC1", type, code, Snapshot).Libelle);
    }

    // PC1 standard lookup: the legacy appends " " + consignePlus even when consignePlus is empty.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_PitsLookup_KeepsLegacyTrailingSpace_AcFr19_5()
    {
        Assert.Equal("Préchauffage normal ", LibelleConsigneResolver.Resolve("PC1", 6, "012", Snapshot).Libelle);
    }

    // PC1 type 6 "particulier": a leading digit >= 2 is swapped for "0" and its particular libellé is
    // appended; the reported DateMaj is the later of the two rows consulted.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_PitsParticulier_AppendsParticularLibelle_AcFr19_5()
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve("PC1", 6, "312", Snapshot);

        Assert.Equal("Préchauffage normal Particulier 3", resolved.Libelle);
        Assert.Equal(Newer, resolved.LatestDateMaj);
    }

    // A leading "1" is below the particular threshold: no suffix, but the "0" swap still happens.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_PitsLeadingOne_SwapsWithoutParticular_AcFr19_5()
    {
        Assert.Equal("Préchauffage normal ", LibelleConsigneResolver.Resolve("PC1", 6, "112", Snapshot).Libelle);
    }

    // The call-site rule: a blank libellé (here a PITS row whose Libelle is only spaces) becomes "?".
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_BlankLibelle_BecomesQuestionMark_AcFr19_5()
    {
        Assert.Equal("?", LibelleConsigneResolver.Resolve("PC1", 5, "1", Snapshot).Libelle);
    }

    // SQL '=' on a French_CI_AS column ignores case, so "x" matches both "X" and "x"; the first in snapshot
    // order wins. The legacy C# switch on the section itself stays case-sensitive.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_CaseInsensitiveCodeMatch_TakesFirstRow_AcFr19_5()
    {
        Assert.Equal("Premier", LibelleConsigneResolver.Resolve("LA1", 8, "x", Snapshot).Libelle);
        Assert.Equal("?", LibelleConsigneResolver.Resolve("la1", 8, "x", Snapshot).Libelle);
    }

    // Matrix row "Degazage" and the H2Coulee buckets of the detail row matched by profile and section.
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("1", 1.0, "0")]
    [InlineData("1", 2.5, "0")]
    [InlineData("1", 3.0, "2,50h")]
    [InlineData("1", 3.5, "3,00h")]
    [InlineData("1", 4.5, "4,00h")]
    [InlineData("1", 6.0, "5,50h")]
    [InlineData("2", 6.0, "0")]
    public void Resolve_Degazage_PicksH2Bucket_AcFr19_5(string code, double h2, string expected)
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve(
            "XA1", 22, code, Snapshot, profilProduit: "rd", diametreProduit: 150.0m, h2Coulee: (decimal)h2);

        Assert.Equal(expected, resolved.Libelle);
        Assert.Null(resolved.LatestDateMaj);
    }

    // The detail range is (SectionMin, SectionMax]: a diameter outside it, or another profile, gives "0".
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData("RD", 100.0)]
    [InlineData("RD", 200.1)]
    [InlineData("CR", 150.0)]
    public void Resolve_DegazageOutsideDetailRange_ReturnsZero_AcFr19_5(string profil, double diametre)
    {
        Assert.Equal(
            "0",
            LibelleConsigneResolver.Resolve("XA1", 22, "1", Snapshot, profil, (decimal)diametre, 6.0m).Libelle);
    }

    // Matrix row "Degazage, pits section absent or H2Coulee null" (assumed, unverified: legacy would throw).
    [Theory]
    [Trait("AC", "FR19-5")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Resolve_DegazageMissingInput_ReturnsQuestionMark_AcFr19_5(bool hasDiametre, bool hasH2)
    {
        LibelleConsigneResolution resolved = LibelleConsigneResolver.Resolve(
            "XA1", 22, "1", Snapshot, "RD", hasDiametre ? 150.0m : null, hasH2 ? 6.0m : null);

        Assert.Equal("?", resolved.Libelle);
    }

    // AC "empty snapshot": lookup-based libellés are "?", computed ones are still produced.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void Resolve_EmptySnapshot_LookupsGiveQuestionMarkComputedStillWork_AcFr19_5()
    {
        Assert.Equal("?", LibelleConsigneResolver.Resolve("LA1", 7, "0", ConsigneReferenceData.Empty).Libelle);
        Assert.Equal("1250", LibelleConsigneResolver.Resolve("PC1", 10, "205", ConsigneReferenceData.Empty).Libelle);
    }

    // ConsignesMapper fills LibelleConsigne on every row it emits: with no snapshot, never null.
    [Fact]
    [Trait("AC", "FR19-5")]
    public void ConsignesMapper_SetsLibelleOnEveryRow_AcFr19_5()
    {
        L_D_KAPE22 source = ReferenceKape22();

        List<L_D_CONSIGNES> rows = ConsignesMapper.Map(
            source,
            SectionChargeChutageMapper.Map(source),
            SectionChargeDecoupeMapper.Map(source),
            SectionChargeLingotMapper.Map(source),
            SectionChargePitsMapper.Map(source),
            SectionChargePoidsMetriqueMapper.Map(source),
            SectionChargeRefroidissoirsMapper.Map(source),
            SectionChargeSvtMapper.Map(source));

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.LibelleConsigne)));
        Assert.Contains(rows, row => row.CodeOperation == "XC1" && row.TypeConsigne == 0 && row.LibelleConsigne == "0,0");
    }

    private static SectionConsigneReference Section(string section, int type, string code, string libelle, DateTime? dateMaj = null) =>
        new() { CodeConsigne = code, Consignes = type, DateMaj = dateMaj ?? Older, Libelle = libelle, Section = section };
}
