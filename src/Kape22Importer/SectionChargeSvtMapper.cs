using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_SECTIONCHARGE_SVT (Story 4.1), no
// reflective property lookup and no database access (AD-2, AC-FR19-1) - a pure function of its single
// input. The table has no measure columns, only its key (Story 4.1); every assignment below is sourced
// by the Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_SVT). Returns null when
// the section does not concern this OF - CodeOpeSVT or RangOpeSVT empty (annex's per-OF applicability
// rule, AC-FR19-2).
public static class SectionChargeSvtMapper
{
    public static L_D_SECTIONCHARGE_SVT? Map(L_D_KAPE22 source)
    {
        if (!SectionChargeApplicability.IsApplicable(source.CodeOpeSVT, source.RangOpeSVT))
        {
            return null;
        }

        return new L_D_SECTIONCHARGE_SVT
        {
            CodeOperation = source.CodeOpeSVT!,
            OF = source.OF,
            RangOperation = source.RangOpeSVT!,
        };
    }
}
