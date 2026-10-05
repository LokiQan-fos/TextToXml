namespace Kape22Importer.Persistence;

// Story 6.10 review (D-1/P-9): minimal database-first entity for AscoLSI.dbo.L_D_PRODUITS_OUTIL (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on (Zone, OF). Only the key is mapped; the D34 replace
// deletes the OF's rows, as the legacy DeleteOF does, and never inserts any. OF is NVARCHAR(12), zero-padded
// like every downstream OF (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_PRODUITS_OUTIL
{
    public string OF { get; set; } = null!;

    public string Zone { get; set; } = null!;
}
