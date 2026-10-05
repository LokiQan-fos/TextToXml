namespace Kape22Importer.Persistence;

// Story 6.10 (D34): read-only, minimal database-first entity for AscoLSI.dbo.L_D_PSO (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on (Coulee, NumeroLingot). Only the key and OF are mapped;
// the importer reads it as a re-send precondition and never writes it. OF is NCHAR(12), zero-padded like
// every downstream OF (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_PSO
{
    public string Coulee { get; set; } = null!;

    public int NumeroLingot { get; set; }

    public string? OF { get; set; }
}
