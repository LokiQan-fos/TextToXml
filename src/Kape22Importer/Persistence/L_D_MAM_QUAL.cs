namespace Kape22Importer.Persistence;

// Story 6.10 review (D-1/P-9): minimal database-first entity for AscoLSI.dbo.L_D_MAM_QUAL (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on OF. Only the key is mapped; the D34 replace deletes the
// OF's row, as the legacy DeleteOF does, and never inserts one. OF is NVARCHAR(12), zero-padded like every
// downstream OF (DownstreamOf.Pad).
public class L_D_MAM_QUAL
{
    public string OF { get; set; } = null!;
}
