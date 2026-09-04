using System.Xml.Serialization;

namespace Kape22Importer;

// DTO for the P60 (KAPE22) normalized XML document, generated from Templates/P60.xml by
// scripts/gen.ps1 - do not edit by hand (AR-4). Members follow the P60.xsd xs:sequence /
// descriptor <value> order, NOT CC-4 alphabetical order, so the file stays diffable against
// P60.xml and P60.xsd (risk R-5). A typed Champ is int? because Step 1 omits a blank one
// (PRD D27); an omitted element deserializes to null. Every class is partial: a property
// added by hand goes in a second, alphabetically sorted partial declaration (R-5).

[XmlRoot("file")]
public sealed partial class Kape22File
{
    [XmlElement("header")]
    public Kape22FileHeader Header { get; set; } = new();

    [XmlElement("message")]
    public Kape22FileMessage Message { get; set; } = new();

    [XmlElement("footer")]
    public Kape22FileFooter Footer { get; set; } = new();
}

public sealed partial class Kape22FileHeader
{
    [XmlElement("File")]
    public string File { get; set; } = string.Empty;

    [XmlElement("Date")]
    public string Date { get; set; } = string.Empty;

    [XmlElement("NumeroFichier")]
    public string NumeroFichier { get; set; } = string.Empty;

    [XmlElement("Segment")]
    public string Segment { get; set; } = string.Empty;

    [XmlElement("Emet")]
    public string Emet { get; set; } = string.Empty;

    [XmlElement("Recepteur")]
    public string Recepteur { get; set; } = string.Empty;

    [XmlElement("Filler")]
    public string Filler { get; set; } = string.Empty;
}

public sealed partial class Kape22FileMessage
{
    [XmlElement("File")]
    public string File { get; set; } = string.Empty;

    [XmlElement("Date")]
    public string Date { get; set; } = string.Empty;

    [XmlElement("NumeroFichier")]
    public string NumeroFichier { get; set; } = string.Empty;

    [XmlElement("Segment")]
    public string Segment { get; set; } = string.Empty;

    [XmlElement("Element")]
    public string Element { get; set; } = string.Empty;

    [XmlElement("KAP")]
    public string KAP { get; set; } = string.Empty;

    [XmlElement("Reserve")]
    public string Reserve { get; set; } = string.Empty;

    [XmlElement("Type")]
    public string Type { get; set; } = string.Empty;

    [XmlElement("OF")]
    public string OF { get; set; } = string.Empty;

    [XmlElement("Indice")]
    public int? Indice { get; set; }

    [XmlElement("Client")]
    public string Client { get; set; } = string.Empty;

    [XmlElement("Nuance")]
    public string Nuance { get; set; } = string.Empty;

    [XmlElement("Coulee")]
    public string Coulee { get; set; } = string.Empty;

    [XmlElement("ProfilProduit")]
    public string ProfilProduit { get; set; } = string.Empty;

    [XmlElement("DiametreProduit")]
    public int? DiametreProduit { get; set; }

    [XmlElement("ToleranceMaxSection")]
    public int? ToleranceMaxSection { get; set; }

    [XmlElement("ToleranceMinSection")]
    public int? ToleranceMinSection { get; set; }

    [XmlElement("Epaisseur")]
    public int? Epaisseur { get; set; }

    [XmlElement("ToleranceMaxEpaisseur")]
    public int? ToleranceMaxEpaisseur { get; set; }

    [XmlElement("ToleranceMinEpaisseur")]
    public int? ToleranceMinEpaisseur { get; set; }

    [XmlElement("ClasseDeChute")]
    public string ClasseDeChute { get; set; } = string.Empty;

    [XmlElement("LongueurCD")]
    public int? LongueurCD { get; set; }

    [XmlElement("ToleranceMaxLongueur")]
    public int? ToleranceMaxLongueur { get; set; }

    [XmlElement("ToleranceMinLongueur")]
    public int? ToleranceMinLongueur { get; set; }

    [XmlElement("MarqueCommerciale")]
    public string MarqueCommerciale { get; set; } = string.Empty;

    [XmlElement("NumeroMontage")]
    public string NumeroMontage { get; set; } = string.Empty;

    [XmlElement("CodeDemiProduit")]
    public string CodeDemiProduit { get; set; } = string.Empty;

    [XmlElement("PoidsDemiProduitUnitaire")]
    public int? PoidsDemiProduitUnitaire { get; set; }

    [XmlElement("NombreDemiProduit")]
    public int? NombreDemiProduit { get; set; }

    [XmlElement("AcompteSolde")]
    public string AcompteSolde { get; set; } = string.Empty;

    [XmlElement("PoidsPrevuDemiProduit")]
    public int? PoidsPrevuDemiProduit { get; set; }

    [XmlElement("RangOpePits")]
    public string RangOpePits { get; set; } = string.Empty;

    [XmlElement("CodeOpePits")]
    public string CodeOpePits { get; set; } = string.Empty;

    [XmlElement("LibelleConsignePits")]
    public string LibelleConsignePits { get; set; } = string.Empty;

    [XmlElement("CodeConsignePits")]
    public string CodeConsignePits { get; set; } = string.Empty;

    [XmlElement("H2Coulee")]
    public int? H2Coulee { get; set; }

    [XmlElement("NumeroFour1")]
    public int? NumeroFour1 { get; set; }

    [XmlElement("DateEnfournementFour1_Date")]
    public string DateEnfournementFour1_Date { get; set; } = string.Empty;

    [XmlElement("DateEnfournementFour1_Heure")]
    public string DateEnfournementFour1_Heure { get; set; } = string.Empty;

    [XmlElement("NumeroFour2")]
    public int? NumeroFour2 { get; set; }

    [XmlElement("DateEnfournementFour2_Date")]
    public string DateEnfournementFour2_Date { get; set; } = string.Empty;

    [XmlElement("DateEnfournementFour2_Heure")]
    public string DateEnfournementFour2_Heure { get; set; } = string.Empty;

    [XmlElement("RangOpeLingot")]
    public string RangOpeLingot { get; set; } = string.Empty;

    [XmlElement("CodeOpeLingot")]
    public string CodeOpeLingot { get; set; } = string.Empty;

    [XmlElement("LibelleConsigneLingot")]
    public string LibelleConsigneLingot { get; set; } = string.Empty;

    [XmlElement("CodeConsigneLingot")]
    public string CodeConsigneLingot { get; set; } = string.Empty;

    [XmlElement("ProfileLamine")]
    public string ProfileLamine { get; set; } = string.Empty;

    [XmlElement("SectionLaminage")]
    public int? SectionLaminage { get; set; }

    [XmlElement("ToleranceMaxSection1")]
    public int? ToleranceMaxSection1 { get; set; }

    [XmlElement("ToleranceMinSection1")]
    public int? ToleranceMinSection1 { get; set; }

    [XmlElement("EpaisseurEnLaminage")]
    public int? EpaisseurEnLaminage { get; set; }

    [XmlElement("ToleranceMaxEpaisseur1")]
    public int? ToleranceMaxEpaisseur1 { get; set; }

    [XmlElement("ToleranceMinEpaisseur1")]
    public int? ToleranceMinEpaisseur1 { get; set; }

    [XmlElement("PriseDeFer")]
    public int? PriseDeFer { get; set; }

    [XmlElement("RangOpeChutage")]
    public string RangOpeChutage { get; set; } = string.Empty;

    [XmlElement("CodeOpeChutage")]
    public string CodeOpeChutage { get; set; } = string.Empty;

    [XmlElement("LibelleConsigneChutage")]
    public string LibelleConsigneChutage { get; set; } = string.Empty;

    [XmlElement("CodeConsigneChutage")]
    public string CodeConsigneChutage { get; set; } = string.Empty;

    [XmlElement("Destination")]
    public string Destination { get; set; } = string.Empty;

    [XmlElement("ChutageTete")]
    public int? ChutageTete { get; set; }

    [XmlElement("ChutagePied")]
    public int? ChutagePied { get; set; }

    [XmlElement("RangOpeDecoupe")]
    public string RangOpeDecoupe { get; set; } = string.Empty;

    [XmlElement("CodeOpeDecoupe")]
    public string CodeOpeDecoupe { get; set; } = string.Empty;

    [XmlElement("LibelleConsigneDecoupe")]
    public string LibelleConsigneDecoupe { get; set; } = string.Empty;

    [XmlElement("CodeConsigneDecoupe")]
    public string CodeConsigneDecoupe { get; set; } = string.Empty;

    [XmlElement("LongueurMoyenne")]
    public int? LongueurMoyenne { get; set; }

    [XmlElement("OutilDecoupe")]
    public string OutilDecoupe { get; set; } = string.Empty;

    [XmlElement("RangOpePoidMetrique")]
    public string RangOpePoidMetrique { get; set; } = string.Empty;

    [XmlElement("CodeOpePoidMetrique")]
    public string CodeOpePoidMetrique { get; set; } = string.Empty;

    [XmlElement("LibelleConsignePoidMetrique")]
    public string LibelleConsignePoidMetrique { get; set; } = string.Empty;

    [XmlElement("CodeConsignePoidMetrique")]
    public string CodeConsignePoidMetrique { get; set; } = string.Empty;

    [XmlElement("RangOpeRefroidissoir")]
    public string RangOpeRefroidissoir { get; set; } = string.Empty;

    [XmlElement("CodeOpeRefroidissoir")]
    public string CodeOpeRefroidissoir { get; set; } = string.Empty;

    [XmlElement("LibelleConsigneRefroidissoir")]
    public string LibelleConsigneRefroidissoir { get; set; } = string.Empty;

    [XmlElement("CodeConsigneRefroidissoir")]
    public string CodeConsigneRefroidissoir { get; set; } = string.Empty;

    [XmlElement("ReserveRefroidissoir")]
    public string ReserveRefroidissoir { get; set; } = string.Empty;

    [XmlElement("MatriculeClient")]
    public int? MatriculeClient { get; set; }

    [XmlElement("RefroidissementBloom")]
    public string RefroidissementBloom { get; set; } = string.Empty;

    [XmlElement("NombreLingotsFour1")]
    public int? NombreLingotsFour1 { get; set; }

    [XmlElement("NombreLingotsFour2")]
    public int? NombreLingotsFour2 { get; set; }

    [XmlElement("ReserveRefroidissoir2")]
    public string ReserveRefroidissoir2 { get; set; } = string.Empty;

    [XmlElement("OFOrigin")]
    public string OFOrigin { get; set; } = string.Empty;

    [XmlElement("OForiginInterne")]
    public string OForiginInterne { get; set; } = string.Empty;

    [XmlElement("OFDestination")]
    public string OFDestination { get; set; } = string.Empty;

    [XmlElement("OFDestinationInterne")]
    public string OFDestinationInterne { get; set; } = string.Empty;

    [XmlElement("NuanceMarquage")]
    public string NuanceMarquage { get; set; } = string.Empty;

    [XmlElement("GazScarfing")]
    public string GazScarfing { get; set; } = string.Empty;

    [XmlElement("OxygeneSuperieur")]
    public string OxygeneSuperieur { get; set; } = string.Empty;

    [XmlElement("OxygeneInferieur")]
    public string OxygeneInferieur { get; set; } = string.Empty;

    [XmlElement("OxygeneLatent")]
    public string OxygeneLatent { get; set; } = string.Empty;

    [XmlElement("VitesseV1")]
    public string VitesseV1 { get; set; } = string.Empty;

    [XmlElement("VitesseV2")]
    public string VitesseV2 { get; set; } = string.Empty;

    [XmlElement("VitesseV3")]
    public string VitesseV3 { get; set; } = string.Empty;

    [XmlElement("LongueurScarfingPied")]
    public string LongueurScarfingPied { get; set; } = string.Empty;

    [XmlElement("LongueurScarfingTete")]
    public string LongueurScarfingTete { get; set; } = string.Empty;

    [XmlElement("MiseAuMille")]
    public string MiseAuMille { get; set; } = string.Empty;

    [XmlElement("ReserveRefroidissoir3")]
    public string ReserveRefroidissoir3 { get; set; } = string.Empty;

    [XmlElement("RangOpeSVT")]
    public string RangOpeSVT { get; set; } = string.Empty;

    [XmlElement("CodeOpeSVT")]
    public string CodeOpeSVT { get; set; } = string.Empty;

    [XmlElement("LibelleConsigneSVT")]
    public string LibelleConsigneSVT { get; set; } = string.Empty;

    [XmlElement("CodeConsigneSVT")]
    public string CodeConsigneSVT { get; set; } = string.Empty;

    [XmlElement("ReserveSVT")]
    public string ReserveSVT { get; set; } = string.Empty;
}

public sealed partial class Kape22FileFooter
{
    [XmlElement("File")]
    public string File { get; set; } = string.Empty;

    [XmlElement("Date")]
    public string Date { get; set; } = string.Empty;

    [XmlElement("NumeroFichier")]
    public string NumeroFichier { get; set; } = string.Empty;

    [XmlElement("Segment")]
    public string Segment { get; set; } = string.Empty;

    [XmlElement("Records")]
    public string Records { get; set; } = string.Empty;

    [XmlElement("Filler")]
    public string Filler { get; set; } = string.Empty;
}
