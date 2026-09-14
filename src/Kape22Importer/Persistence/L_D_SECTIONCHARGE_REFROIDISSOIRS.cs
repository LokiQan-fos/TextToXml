namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_SECTIONCHARGE_REFROIDISSOIRS (Story 4.1): 21 columns,
// Refroidissoirs charge section per (OF, CodeOperation). Shapes are frozen here, there is no migration
// (AR-8, AR-12); sourced from AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from
// memory (risk R-3). The key is the natural composite business key (OF, CodeOperation), not a
// surrogate identity. Only OF, CodeOperation and RangOperation are NOT NULL; every other column is
// nullable. Properties are declared in alphabetical order (CC-4).
public class L_D_SECTIONCHARGE_REFROIDISSOIRS
{
    public string CodeOperation { get; set; } = null!;

    public string? GazScarfing { get; set; }

    public string? LongueurScarfingPied { get; set; }

    public string? LongueurScarfingTete { get; set; }

    public int? MatriculeClient { get; set; }

    public string? MiseAuMille { get; set; }

    public int? NombreLingotsFour1 { get; set; }

    public int? NombreLingotsFour2 { get; set; }

    public string? NuanceMarquage { get; set; }

    public string OF { get; set; } = null!;

    public string? OFDestination { get; set; }

    public string? OFInterne { get; set; }

    public string? OFOrigin { get; set; }

    public string? OxygeneInferieur { get; set; }

    public string? OxygeneLatent { get; set; }

    public string? OxygeneSuperieur { get; set; }

    public string RangOperation { get; set; } = null!;

    public string? RefroidissementBloom { get; set; }

    public string? VitesseV1 { get; set; }

    public string? VitesseV2 { get; set; }

    public string? VitesseV3 { get; set; }
}
