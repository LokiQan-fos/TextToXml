namespace Kape22Importer.Persistence;

// Story 6.10 (D34): read-only, minimal database-first entity for AscoLSI.dbo.L_D_FOURS (AFV004-LSI
// sys.columns/sys.indexes, 2026-10-05). Only the key and OFEnCours, the OF a furnace currently holds, are
// mapped; the importer reads it as a re-send precondition and never writes it. OFEnCours is NCHAR(12),
// zero-padded like every downstream OF (DownstreamOf.Pad). Properties in alphabetical order (CC-4).
public class L_D_FOURS
{
    public string Id { get; set; } = null!;

    public string? OFEnCours { get; set; }
}
