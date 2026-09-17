namespace Kape22Importer;

// Story 4.3-bis: the single scale-conversion point for the 21 KAPE22 dimension/tolerance columns that
// land in narrow DECIMAL EF columns (annexe-mapping-dispatch-epic4.md, Story 4.2-bis's Scale column) -
// a small cross-mapper helper, same shape as the existing SectionChargeApplicability precedent (AD-2:
// internal static, zero reflection, zero database access). Division by an exact power of ten is exact
// for decimal, so no Math.Round is needed; scale 0 is a real value (identity), not "no conversion".
internal static class DecimalScale
{
    private static readonly decimal[] Pow10 = [1m, 10m, 100m, 1000m];

    public static decimal Apply(int rawValue, int scale) => rawValue / Pow10ForScale(scale);

    // Every call site today passes a literal 0-3 (Story 4.2-bis's annex Scale column caps out at 3), so
    // this is unreachable in practice - but a future out-of-range scale should fail with a descriptive
    // message naming the offending value, not a bare, unlabeled IndexOutOfRangeException.
    private static decimal Pow10ForScale(int scale) => scale is >= 0 && scale < Pow10.Length
        ? Pow10[scale]
        : throw new ArgumentOutOfRangeException(nameof(scale), scale, $"Scale must be between 0 and {Pow10.Length - 1}.");

    public static decimal? Apply(int? rawValue, int scale) => rawValue is int value ? Apply(value, scale) : null;
}
