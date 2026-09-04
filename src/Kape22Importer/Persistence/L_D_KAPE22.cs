using System;

namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_KAPE22 (Annexe C.1): 92 columns, one row per imported
// KAPE22 file. Shapes are frozen here, there is no migration (AR-8). NOT NULL columns besides the
// identity Id are Client, Coulee, DateReception, Indice, NumeroFichier, Nuance, OF, Type; every other
// column is nullable. Column string lengths are applied in Story 2.5 from scripts/schema/.
// Properties are declared in alphabetical order (CC-4).
public class L_D_KAPE22
{
    public string? AcompteSolde { get; set; }

    public int? ChutagePied { get; set; }

    public int? ChutageTete { get; set; }

    public string? ClasseDeChute { get; set; }

    public string Client { get; set; } = null!;

    public string? CodeConsigneChutage { get; set; }

    public string? CodeConsigneDecoupe { get; set; }

    public string? CodeConsigneLingot { get; set; }

    public string? CodeConsignePits { get; set; }

    public string? CodeConsignePoidMetrique { get; set; }

    public string? CodeConsigneRefroidissoir { get; set; }

    public string? CodeConsigneSVT { get; set; }

    public string? CodeDemiProduit { get; set; }

    public string? CodeOpeChutage { get; set; }

    public string? CodeOpeDecoupe { get; set; }

    public string? CodeOpeLingot { get; set; }

    public string? CodeOpePits { get; set; }

    public string? CodeOpePoidMetrique { get; set; }

    public string? CodeOpeRefroidissoir { get; set; }

    public string? CodeOpeSVT { get; set; }

    public string Coulee { get; set; } = null!;

    public DateTime? DateEnfournementFour1 { get; set; }

    public DateTime? DateEnfournementFour2 { get; set; }

    public DateTime DateReception { get; set; }

    public string? Destination { get; set; }

    public int? DiametreProduit { get; set; }

    public int? Epaisseur { get; set; }

    public int? EpaisseurEnLaminage { get; set; }

    public string? GazScarfing { get; set; }

    public int? H2Coulee { get; set; }

    public int Id { get; set; }

    public int Indice { get; set; }

    public string? LibelleConsigneChutage { get; set; }

    public string? LibelleConsigneDecoupe { get; set; }

    public string? LibelleConsigneLingot { get; set; }

    public string? LibelleConsignePits { get; set; }

    public string? LibelleConsignePoidMetrique { get; set; }

    public string? LibelleConsigneRefroidissoir { get; set; }

    public string? LibelleConsigneSVT { get; set; }

    public int? LongueurCD { get; set; }

    public int? LongueurMoyenne { get; set; }

    public string? LongueurScarfingPied { get; set; }

    public string? LongueurScarfingTete { get; set; }

    public string? MarqueCommerciale { get; set; }

    public int? MatriculeClient { get; set; }

    public string? MiseAuMille { get; set; }

    public int? NombreDemiProduit { get; set; }

    public int? NombreLingotsFour1 { get; set; }

    public int? NombreLingotsFour2 { get; set; }

    public string Nuance { get; set; } = null!;

    public string? NuanceMarquage { get; set; }

    public string NumeroFichier { get; set; } = null!;

    public int? NumeroFour1 { get; set; }

    public int? NumeroFour2 { get; set; }

    public string? NumeroMontage { get; set; }

    public string OF { get; set; } = null!;

    public string? OFDestination { get; set; }

    public string? OFDestinationInterne { get; set; }

    public string? OFOrigin { get; set; }

    public string? OForiginInterne { get; set; }

    public string? OutilDecoupe { get; set; }

    public string? OxygeneInferieur { get; set; }

    public string? OxygeneLatent { get; set; }

    public string? OxygeneSuperieur { get; set; }

    public int? PoidsDemiProduitUnitaire { get; set; }

    public int? PoidsPrevuDemiProduit { get; set; }

    public int? PriseDeFer { get; set; }

    public string? ProfileLamine { get; set; }

    public string? ProfilProduit { get; set; }

    public string? RangOpeChutage { get; set; }

    public string? RangOpeDecoupe { get; set; }

    public string? RangOpeLingot { get; set; }

    public string? RangOpePits { get; set; }

    public string? RangOpePoidMetrique { get; set; }

    public string? RangOpeRefroidissoir { get; set; }

    public string? RangOpeSVT { get; set; }

    public string? RefroidissementBloom { get; set; }

    public int? SectionLaminage { get; set; }

    public int? ToleranceMaxEpaisseur { get; set; }

    public int? ToleranceMaxEpaisseur1 { get; set; }

    public int? ToleranceMaxLongueur { get; set; }

    public int? ToleranceMaxSection { get; set; }

    public int? ToleranceMaxSection1 { get; set; }

    public int? ToleranceMinEpaisseur { get; set; }

    public int? ToleranceMinEpaisseur1 { get; set; }

    public int? ToleranceMinLongueur { get; set; }

    public int? ToleranceMinSection { get; set; }

    public int? ToleranceMinSection1 { get; set; }

    public string Type { get; set; } = null!;

    public string? VitesseV1 { get; set; }

    public string? VitesseV2 { get; set; }

    public string? VitesseV3 { get; set; }
}
