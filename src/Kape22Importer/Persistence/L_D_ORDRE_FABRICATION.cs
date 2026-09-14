using System;

namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_ORDRE_FABRICATION (Story 4.1): 45 columns, one row per
// Ordre de Fabrication. Shapes are frozen here, there is no migration (AR-8, AR-12); sourced from
// AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3). The key is
// the natural business key OF, not a surrogate identity. Nullable columns are Coulee, DateDebut,
// DateDebutLaminage, DateEVC, DateFin, DateFinLaminage, NombreLingotsWagon1Four1,
// NombreLingotsWagon1Four2, NombreLingotsWagon2Four1, NombreLingotsWagon2Four2, OFOrigine,
// PoidsDemiProduitUnitaire, PoidsPrevuDemiProduit, SensLaminage, SensLaminageGPAO, SuiviDeZoneZone,
// TemperatureScarfing, TemperatureT03, TemperatureT07; every other column is NOT NULL. Properties are
// declared in alphabetical order (CC-4).
public class L_D_ORDRE_FABRICATION
{
    public string AcompteSolde { get; set; } = null!;

    public string ClasseDeChute { get; set; } = null!;

    public string Client { get; set; } = null!;

    public string CodeDemiProduit { get; set; } = null!;

    public string? Coulee { get; set; }

    public DateTime? DateDebut { get; set; }

    public DateTime? DateDebutLaminage { get; set; }

    public DateTime? DateEVC { get; set; }

    public DateTime? DateFin { get; set; }

    public DateTime? DateFinLaminage { get; set; }

    public DateTime DateMaj { get; set; }

    public DateTime DateReception { get; set; }

    public decimal DiametreProduit { get; set; }

    public decimal Epaisseur { get; set; }

    public int Etat { get; set; }

    public int Indice { get; set; }

    public decimal LongueurCD { get; set; }

    public string MarqueCommerciale { get; set; } = null!;

    public int NombreDemiProduit { get; set; }

    public int? NombreLingotsWagon1Four1 { get; set; }

    public int? NombreLingotsWagon1Four2 { get; set; }

    public int? NombreLingotsWagon2Four1 { get; set; }

    public int? NombreLingotsWagon2Four2 { get; set; }

    public string Nuance { get; set; } = null!;

    public string NumeroFichier { get; set; } = null!;

    public string NumeroMontage { get; set; } = null!;

    public string OF { get; set; } = null!;

    public string? OFOrigine { get; set; }

    public decimal? PoidsDemiProduitUnitaire { get; set; }

    public int PoidsPesee { get; set; }

    public decimal? PoidsPrevuDemiProduit { get; set; }

    public string ProfilProduit { get; set; } = null!;

    public string? SensLaminage { get; set; }

    public string? SensLaminageGPAO { get; set; }

    public string? SuiviDeZoneZone { get; set; }

    public string? TemperatureScarfing { get; set; }

    public int? TemperatureT03 { get; set; }

    public int? TemperatureT07 { get; set; }

    public decimal ToleranceMaxEpaisseur { get; set; }

    public decimal ToleranceMaxLongueur { get; set; }

    public decimal ToleranceMaxSection { get; set; }

    public decimal ToleranceMinEpaisseur { get; set; }

    public decimal ToleranceMinLongueur { get; set; }

    public decimal ToleranceMinSection { get; set; }

    public string Type { get; set; } = null!;
}
