namespace Kape22Importer.Persistence;

// Story 6.10 review (D-1/P-9): minimal database-first entity for AscoLSI.dbo.L_D_OF_SUIVI (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on OF. The D34 replace deletes the OF's row and moves every
// later Rang up by one, as the legacy DeleteOF does. OF is NCHAR(12), zero-padded like every downstream OF
// (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_OF_SUIVI
{
    public string OF { get; set; } = null!;

    public int Rang { get; set; }
}
