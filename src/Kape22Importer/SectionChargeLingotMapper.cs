using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_SECTIONCHARGE_LINGOT (Story 4.1),
// one property per line, no reflective property lookup and no database access (AD-2, AC-FR19-1) - a
// pure function of its single input. Every assignment below is sourced by the Story 4.2 annex
// (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_LINGOT); nothing here invents a rule the annex
// does not document (AC-FR17-4). Returns null when the section does not concern this OF - CodeOpeLingot
// or RangOpeLingot empty (annex's per-OF applicability rule, AC-FR19-2).
public static class SectionChargeLingotMapper
{
    public static L_D_SECTIONCHARGE_LINGOT? Map(L_D_KAPE22 source)
    {
        if (!SectionChargeApplicability.IsApplicable(source.CodeOpeLingot, source.RangOpeLingot))
        {
            return null;
        }

        return new L_D_SECTIONCHARGE_LINGOT
        {
            CodeOperation = source.CodeOpeLingot!,
            EpaisseurEnLaminage = source.EpaisseurEnLaminage,
            OF = source.OF,
            PriseDeFer = source.PriseDeFer,
            ProfileLamine = source.ProfileLamine,
            RangOperation = source.RangOpeLingot!,
            SectionLaminage = source.SectionLaminage,
            ToleranceMaxEpaisseur = source.ToleranceMaxEpaisseur1,
            ToleranceMaxSection = source.ToleranceMaxSection1,
            ToleranceMinEpaisseur = source.ToleranceMinEpaisseur1,
            ToleranceMinSection = source.ToleranceMinSection1,

            // PriseDeFerEpaisseur, PriseDeFerEpaisseurGPAO, PriseDeFerHauteur, PriseDeFerHauteurGPAO,
            // PriseDeFerSection, PriseDeFerSectionGPAO, Programme, ProgrammeGPAO: assumed, unverified -
            // computed by legacy OrdreDeFabricationManager.ComputePriseDeFer via a reference-table lookup
            // (montage + profil + section), not a KAPE22 field, so out of a pure mapper's reach (AD-2).
            // Left at their CLR default (deferred-work.md § "story-4.2 mapping annex", AC-FR19-4).
        };
    }
}
