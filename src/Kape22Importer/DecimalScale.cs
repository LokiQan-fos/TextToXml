namespace Kape22Importer;

// Story 4.3-bis: the single scale-conversion point for the 21 KAPE22 dimension/tolerance columns that
// land in narrow DECIMAL EF columns (annexe-mapping-dispatch-epic4.md, Story 4.2-bis's Scale column) -
// a small cross-mapper helper, same shape as the existing SectionChargeApplicability precedent (AD-2:
// internal static, zero reflection, zero database access). Division by an exact power of ten is exact
// for decimal, so no Math.Round is needed; scale 0 is a real value (identity), not "no conversion".
internal static class DecimalScale
{
    private static readonly decimal[] Pow10 = [1m, 10m, 100m, 1000m];

    public static decimal Apply(int rawValue, int scale) => rawValue / Pow10[scale];

    public static decimal? Apply(int? rawValue, int scale) => rawValue is int value ? Apply(value, scale) : null;
}
