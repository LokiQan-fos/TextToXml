namespace Kape22Importer.Persistence;

// Story 6.10 (D34): read-only, minimal database-first entity for AscoLSI.dbo.L_D_PLANS_FOURS (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05), keyed on (FourId, Position). Only the key and OF are mapped; the
// importer reads it as a re-send precondition and never writes it. OF is NCHAR(12), zero-padded like
// every downstream OF (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_PLANS_FOURS
{
    public string FourId { get; set; } = null!;

    public string? OF { get; set; }

    public short Position { get; set; }
}
