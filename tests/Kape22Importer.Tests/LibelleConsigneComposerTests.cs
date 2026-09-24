using System.Collections.Generic;
using Kape22Importer.Persistence;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 4.13 (AC-FR19-6): LibelleConsigneComposer is the pure port of the legacy
// OrdreFabrication.BuildLibelleConsigne (Desktop/kape22/OrdreFabrication.cs:700-1222). Each expected value
// is the ConsigneGPAO=0 composite label production holds for OF 2039771 on a section no operator edited.
// Written test-first (CC-1). Unit-only (AR-12).
[Trait("Category", TestCategory.Unit)]
public class LibelleConsigneComposerTests
{
    [Fact]
    [Trait("AC", "FR19-6")]
    public void Pits_ProductionLabels_ReproducesLegacyCompositeWithDegreesAndNewLine_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows(
            (12, "Lingot Froid "), (10, "1250"), (11, "1200"), (5, "Pas de maintien exigé "), (6, "?"));

        string? libelle = LibelleConsigneComposer.Pits(rows);

        Assert.Equal("Lingot Froid \t\t1250°C\t\t1200°C\t\tPas de maintien exigé \n?", libelle);
    }

    // The type 6 label is appended only when it is non-empty (OrdreFabrication.cs:768).
    [Fact]
    [Trait("AC", "FR19-6")]
    public void Pits_EmptyTypeSixLabel_AppendsNoNewLine_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows((12, "A"), (10, "1"), (11, "2"), (5, "B"), (6, string.Empty));

        Assert.Equal("A\t\t1°C\t\t2°C\t\tB", LibelleConsigneComposer.Pits(rows));
    }

    // Without a type 5 row the legacy dereferences null inside the try, so the type 13 label is never
    // assigned (OrdreFabrication.cs:764-781): null tells the caller to leave the row as it is.
    [Fact]
    [Trait("AC", "FR19-6")]
    public void Pits_NoTypeFiveRow_ReturnsNull_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows((12, "A"), (10, "1"), (11, "2"), (6, "C"));

        Assert.Null(LibelleConsigneComposer.Pits(rows));
    }

    [Fact]
    [Trait("AC", "FR19-6")]
    public void Lingot_ProductionLabels_ReproducesLegacyComposite_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows(
            (15, "1070"), (7, "Pas de scarfing"), (8, "?"), (9, "Descente serrée - vitesse lente"));

        Assert.Equal(
            "1070\t\tPas de scarfing\t\t?\t\tDescente serrée - vitesse lente\t\t",
            LibelleConsigneComposer.Lingot(rows));
    }

    // A type the section did not produce contributes nothing, not even its separator: the legacy Find
    // returns null and the caught NullReferenceException appends string.Empty.
    [Fact]
    [Trait("AC", "FR19-6")]
    public void Lingot_MissingType_ContributesNothing_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows((15, "1070"), (9, "C2"));

        Assert.Equal("1070\t\tC2\t\t", LibelleConsigneComposer.Lingot(rows));
    }

    [Fact]
    [Trait("AC", "FR19-6")]
    public void Chutage_ProductionLabels_ReproducesLegacyComposite_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows(
            (0, "0,0"), (1, "0"), (2, "Sans éboutage (cisaillé 1500 T)"), (3, "Aucun prélèvement"), (4, "M"));

        Assert.Equal(
            "0,0\t\t0\t\tSans éboutage (cisaillé 1500 T)\t\tAucun prélèvement\t\tM\t\t",
            LibelleConsigneComposer.Chutage(rows));
    }

    // Both XP1 blocks, production values: type 29 is not part of the size-18 composite, and the type 24
    // label read is its own "?", resolved before any composite exists.
    [Fact]
    [Trait("AC", "FR19-6")]
    public void Decoupe_BothBlocks_ReproducesBothLegacyComposites_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows(
            (16, "11,400"), (17, "11,400"), (24, "?"), (25, "."), (26, "11,400"), (27, "BC"), (28, "SAN"), (29, "A"));

        (string sizeTwelve, string sizeEighteen) = LibelleConsigneComposer.Decoupe(rows);

        Assert.Equal("11,400\t\t11,400\t\t", sizeTwelve);
        Assert.Equal("?\t\t.\t\t11,400\t\tBC\t\tSAN\t\t", sizeEighteen);
    }

    // OF 2039771 has no XP9 section: the expected value is the production composite of OF 9000064.
    [Fact]
    [Trait("AC", "FR19-6")]
    public void PoidsMetrique_ProductionLabels_ReproducesLegacyComposite_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows((18, "0,636"), (19, "06"), (20, "06"));

        Assert.Equal("0,636\t\t06\t\t06\t\t", LibelleConsigneComposer.PoidsMetrique(rows));
    }

    [Fact]
    [Trait("AC", "FR19-6")]
    public void Refroidissoirs_ProductionLabels_ReproducesLegacyComposite_AcFr19_6()
    {
        List<L_D_CONSIGNES> rows = Rows((21, "Refroidissement à l'air"), (22, "0"), (23, "Pas de consigne"));

        Assert.Equal(
            "Refroidissement à l'air\t\t0\t\tPas de consigne\t\t",
            LibelleConsigneComposer.Refroidissoirs(rows));
    }

    private static List<L_D_CONSIGNES> Rows(params (int TypeConsigne, string Libelle)[] labels)
    {
        List<L_D_CONSIGNES> rows = [];
        foreach ((int typeConsigne, string libelle) in labels)
        {
            rows.Add(new L_D_CONSIGNES { LibelleConsigne = libelle, TypeConsigne = typeConsigne });
        }

        return rows;
    }
}
