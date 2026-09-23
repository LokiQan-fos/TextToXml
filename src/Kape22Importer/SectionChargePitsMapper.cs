using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_SECTIONCHARGE_PITS (Story 4.1), one
// property per line, no reflective property lookup and no database access (AD-2, AC-FR19-1) - a pure
// function of its single input. Every assignment below is sourced or ruled by the Story 4.2 annex
// (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_PITS); nothing here invents a rule the annex does
// not document (AC-FR17-4). Returns null when the section does not concern this OF - CodeOpePits or
// RangOpePits empty (annex's per-OF applicability rule, AC-FR19-2).
public static class SectionChargePitsMapper
{
    public static L_D_SECTIONCHARGE_PITS? Map(L_D_KAPE22 source)
    {
        if (!SectionChargeApplicability.IsApplicable(source.CodeOpePits, source.RangOpePits))
        {
            return null;
        }

        return new L_D_SECTIONCHARGE_PITS
        {
            CodeOperation = source.CodeOpePits!,
            H2Coulee = DecimalScale.Apply(source.H2Coulee, 1),
            NumeroFour1 = source.NumeroFour1,
            NumeroFour2 = source.NumeroFour2,
            OF = DownstreamOf.Pad(source.OF),
            RangOperation = source.RangOpePits!,

            // DateEnfournementFour1, DateEnfournementFour2: NULL by rule - deliberately not mapped in the
            // legacy MappingTemplate ("Enlevé car empêche d'enfourner", annex evidence). Not sourced from
            // KAPE22 despite KAPE22 itself carrying DateEnfournementFour1/2 fields.

            // DateDefournementFour1, DateDefournementFour2: assumed, unverified - no source identified in
            // the legacy read for a P60 dispatch (deferred-work.md § "story-4.2 mapping annex", AC-FR19-4).
        };
    }
}
