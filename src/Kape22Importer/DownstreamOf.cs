namespace Kape22Importer;

// Legacy quirk, confirmed against production 2026-09-22: L_D_KAPE22.OF and L_D_LOG_COMMANDE.OF stay
// unpadded, but every one of the 9 downstream dispatch tables (L_D_ORDRE_FABRICATION,
// L_D_SECTIONCHARGE_*, L_D_CONSIGNES) stores OF explicitly zero-padded to 12 digits - a real value the
// legacy application wrote, not an artifact of the NCHAR(12) column type (which would right-pad with
// spaces, not zeros, if left to its own default). Kape22ProductionDataParityTests found the gap: every
// downstream mapper used to copy L_D_KAPE22.OF verbatim.
internal static class DownstreamOf
{
    public static string Pad(string of) => of.PadLeft(12, '0');
}
