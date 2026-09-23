using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_SECTIONCHARGE_CHUTAGE (Story 4.1),
// one property per line, no reflective property lookup and no database access (AD-2, AC-FR19-1) - a
// pure function of its single input. Every assignment below is sourced by the Story 4.2 annex
// (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_CHUTAGE); nothing here invents a rule the annex
// does not document (AC-FR17-4). Returns null when the section does not concern this OF - CodeOpeChutage
// or RangOpeChutage empty (annex's per-OF applicability rule, AC-FR19-2).
public static class SectionChargeChutageMapper
{
    public static L_D_SECTIONCHARGE_CHUTAGE? Map(L_D_KAPE22 source)
    {
        if (!SectionChargeApplicability.IsApplicable(source.CodeOpeChutage, source.RangOpeChutage))
        {
            return null;
        }

        return new L_D_SECTIONCHARGE_CHUTAGE
        {
            ChutagePied = DecimalScale.Apply(source.ChutagePied, 2),
            ChutageTete = DecimalScale.Apply(source.ChutageTete, 2),
            CodeOperation = source.CodeOpeChutage!,
            Destination = source.Destination,
            OF = DownstreamOf.Pad(source.OF),
            RangOperation = source.RangOpeChutage!,
        };
    }
}
