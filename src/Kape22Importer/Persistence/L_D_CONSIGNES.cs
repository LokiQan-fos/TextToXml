namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_CONSIGNES (Story 4.1): 7 columns, operating instructions
// per (OF, CodeOperation, TypeConsigne, ConsigneGPAO). Shapes are frozen here, there is no migration
// (AR-8, AR-12); sourced from AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from
// memory (risk R-3). The key is the natural composite business key (OF, CodeOperation, TypeConsigne,
// ConsigneGPAO), not a surrogate identity. Only LibelleConsigne is nullable; every other column is
// NOT NULL. Properties are declared in alphabetical order (CC-4).
public class L_D_CONSIGNES
{
    public string CodeConsigne { get; set; } = null!;

    public string CodeOperation { get; set; } = null!;

    public bool ConsigneGPAO { get; set; }

    public string? LibelleConsigne { get; set; }

    public string OF { get; set; } = null!;

    public int SizeCodeConsigne { get; set; }

    public int TypeConsigne { get; set; }
}
