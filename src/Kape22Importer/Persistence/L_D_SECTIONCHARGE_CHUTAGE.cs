namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_SECTIONCHARGE_CHUTAGE (Story 4.1): 6 columns, Chutage
// charge section per (OF, CodeOperation). Shapes are frozen here, there is no migration (AR-8, AR-12);
// sourced from AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3).
// The key is the natural composite business key (OF, CodeOperation), not a surrogate identity. Only OF,
// CodeOperation and RangOperation are NOT NULL; every other column is nullable. Properties are declared
// in alphabetical order (CC-4).
public class L_D_SECTIONCHARGE_CHUTAGE
{
    public decimal? ChutagePied { get; set; }

    public decimal? ChutageTete { get; set; }

    public string CodeOperation { get; set; } = null!;

    public string? Destination { get; set; }

    public string OF { get; set; } = null!;

    public string RangOperation { get; set; } = null!;
}
