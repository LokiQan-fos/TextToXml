namespace Kape22Importer;

// Story 4.4 (FR-19): the single per-OF applicability rule the 7 SectionCharge*Mapper classes share
// (annexe-mapping-dispatch-epic4.md, per-OF applicability rule, AC-FR17-3) - a L_D_SECTIONCHARGE_*
// table concerns the current OF iff its CodeOperation and RangOperation source fields in L_D_KAPE22 are
// both non-empty. This is a presence rule on the source fields, not a separate business control
// (AC-FR19-2).
internal static class SectionChargeApplicability
{
    public static bool IsApplicable(string? codeOperation, string? rangOperation) =>
        !string.IsNullOrWhiteSpace(codeOperation) && !string.IsNullOrWhiteSpace(rangOperation);
}
