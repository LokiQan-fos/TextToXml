namespace Kape22Importer.Persistence;

// Story 6.10 review (D-1/P-9): minimal database-first entity for AscoLSI.dbo.L_D_REBUT (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on the identity Id. Only the key and OF are mapped; the D34
// replace deletes the OF's rows, as the legacy DeleteOF does, and never inserts any. OF is NCHAR(12),
// zero-padded like every downstream OF (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_REBUT
{
    public int Id { get; set; }

    public string OF { get; set; } = null!;
}
