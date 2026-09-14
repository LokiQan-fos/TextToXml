namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_SECTIONCHARGE_POIDSMETRIQUE (Story 4.1): 3 columns,
// PoidsMetrique charge section per (OF, CodeOperation) - no measure columns in the real schema, only
// the key. Shapes are frozen here, there is no migration (AR-8, AR-12); sourced from AFV004-LSI
// sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3). The key is the natural
// composite business key (OF, CodeOperation), not a surrogate identity. Every column is NOT NULL.
// Properties are declared in alphabetical order (CC-4).
public class L_D_SECTIONCHARGE_POIDSMETRIQUE
{
    public string CodeOperation { get; set; } = null!;

    public string OF { get; set; } = null!;

    public string RangOperation { get; set; } = null!;
}
