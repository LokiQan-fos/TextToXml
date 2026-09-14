using System;

namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_COULEE (Story 4.1): 78 columns, one row per Coulee.
// Shapes are frozen here, there is no migration (AR-8, AR-12); sourced from AFV004-LSI
// sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3). The key is the natural
// business key IdCoulee, not a surrogate identity. Only DateReception, DerniereModif, EtatReception,
// Externe, IdCoulee, NbLingotRestantARefroidir and Nuance are NOT NULL; every other column is nullable.
// Properties are declared in alphabetical order (CC-4).
public class L_D_COULEE
{
    public bool? AnomalieAPC { get; set; }

    public bool? AnomalieAPCRH { get; set; }

    public bool? AnomalieRH { get; set; }

    public bool? AnomaliesCoulee { get; set; }

    public bool? AnomaliesDegazeur { get; set; }

    public bool? AnomaliesDemoulage { get; set; }

    public DateTime? ArriveeEnfournementWagon1 { get; set; }

    public DateTime? ArriveeEnfournementWagon2 { get; set; }

    public string? CodeLivraison { get; set; }

    public bool? CouleeFroide { get; set; }

    public DateTime? DateCOPAPC { get; set; }

    public DateTime? DateCOPCoulee { get; set; }

    public DateTime? DateCOPDemoulage { get; set; }

    public DateTime? DateCOPRH { get; set; }

    public DateTime DateReception { get; set; }

    public DateTime? DebutCoulee { get; set; }

    public DateTime? DebutDemoulage { get; set; }

    public bool? Degazee { get; set; }

    public DateTime? DelaisLivraisonWagon1 { get; set; }

    public DateTime? DelaisLivraisonWagon2 { get; set; }

    public decimal? DensiteCoulee { get; set; }

    public DateTime DerniereModif { get; set; }

    public DateTime? EcartWagon1 { get; set; }

    public DateTime? EcartWagon2 { get; set; }

    public string? Enregistrement { get; set; }

    public bool? EstConformiteCoulee { get; set; }

    public bool? EstEnfournementStandard { get; set; }

    public bool? EstHomogene { get; set; }

    public bool? EstTroisQuartsConforme { get; set; }

    public int EtatReception { get; set; }

    public bool Externe { get; set; }

    public DateTime? FinCoulee { get; set; }

    public DateTime? FinDemDernierLgtWagon1 { get; set; }

    public DateTime? FinDemDernierLgtWagon2 { get; set; }

    public DateTime? HeureArriveeWagon1 { get; set; }

    public DateTime? HeureArriveeWagon2 { get; set; }

    public DateTime? HeureDepartWagon1 { get; set; }

    public DateTime? HeureDepartWagon2 { get; set; }

    public DateTime? HeurePrevuDemoulage { get; set; }

    public decimal? Hydrogene { get; set; }

    public string IdCoulee { get; set; } = null!;

    public bool? LingotPiscine { get; set; }

    public bool? MarqueFroide { get; set; }

    public int? ModeElaboration { get; set; }

    public int NbLingotRestantARefroidir { get; set; }

    public int? NombreLingotAir { get; set; }

    public int? NombreLingotBacVerniculite { get; set; }

    public int? NombreLingotPitsSec { get; set; }

    public int? NombreLingotsWagon1 { get; set; }

    public int? NombreLingotsWagon2 { get; set; }

    public int? NombreTypelingot1 { get; set; }

    public int? NombreTypelingot2 { get; set; }

    public string Nuance { get; set; } = null!;

    public string? NumerosLingotRebutes { get; set; }

    public string? Observation2 { get; set; }

    public string? Observations { get; set; }

    public string? OperateurCoulee { get; set; }

    public string? OperateurDegazeur { get; set; }

    public string? OperateurDemoulage { get; set; }

    public string? Piscinage { get; set; }

    public bool? PiscinageWagon1 { get; set; }

    public bool? PiscinageWagon2 { get; set; }

    public int? PoidsMoyenLingotMere1 { get; set; }

    public int? PoidsMoyenLingotMere2 { get; set; }

    public int? PoidsMoyenLingotMere3 { get; set; }

    public int? PoidsMoyenLingotMere4 { get; set; }

    public int? PoidsUnitaireLingot1 { get; set; }

    public int? PoidsUnitaireLingot2 { get; set; }

    public string? ProgrammeSMQ { get; set; }

    public string? ResponsableTraitement { get; set; }

    public bool? RetardDemoulage { get; set; }

    public string? RetardLivraisonWagon1 { get; set; }

    public string? RetardLivraisonWagon2 { get; set; }

    public bool? SaturationPits { get; set; }

    public bool? SauvetageWagon1 { get; set; }

    public bool? SauvetageWagon2 { get; set; }

    public string? TypeLingot1 { get; set; }

    public string? TypeLingot2 { get; set; }
}
