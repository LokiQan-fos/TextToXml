using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_SECTIONCHARGE_REFROIDISSOIRS (Story
// 4.1), one property per line, no reflective property lookup and no database access (AD-2, AC-FR19-1) -
// a pure function of its single input. Every assignment below is sourced by the Story 4.2 annex
// (annexe-mapping-dispatch-epic4.md § L_D_SECTIONCHARGE_REFROIDISSOIRS); nothing here invents a rule the
// annex does not document (AC-FR17-4). Returns null when the section does not concern this OF -
// CodeOpeRefroidissoir or RangOpeRefroidissoir empty (annex's per-OF applicability rule, AC-FR19-2).
public static class SectionChargeRefroidissoirsMapper
{
    public static L_D_SECTIONCHARGE_REFROIDISSOIRS? Map(L_D_KAPE22 source)
    {
        if (!SectionChargeApplicability.IsApplicable(source.CodeOpeRefroidissoir, source.RangOpeRefroidissoir))
        {
            return null;
        }

        return new L_D_SECTIONCHARGE_REFROIDISSOIRS
        {
            CodeOperation = source.CodeOpeRefroidissoir!,
            GazScarfing = source.GazScarfing,
            LongueurScarfingPied = source.LongueurScarfingPied,
            LongueurScarfingTete = source.LongueurScarfingTete,
            MatriculeClient = source.MatriculeClient,
            MiseAuMille = source.MiseAuMille,
            NombreLingotsFour1 = source.NombreLingotsFour1,
            NombreLingotsFour2 = source.NombreLingotsFour2,
            NuanceMarquage = source.NuanceMarquage,
            OF = source.OF,
            OFDestination = source.OFDestination,
            OFOrigin = source.OFOrigin,
            OxygeneInferieur = source.OxygeneInferieur,
            OxygeneLatent = source.OxygeneLatent,
            OxygeneSuperieur = source.OxygeneSuperieur,
            RangOperation = source.RangOpeRefroidissoir!,
            RefroidissementBloom = source.RefroidissementBloom,
            VitesseV1 = source.VitesseV1,
            VitesseV2 = source.VitesseV2,
            VitesseV3 = source.VitesseV3,

            // OFInterne: assumed, unverified - annex error found at Story 4.4: the Story 4.2 annex marked
            // this column `sourcée` from `KAPE22.OFInterne`, but L_D_KAPE22 (Story 4.1 EF model) has no
            // property of that exact name - only OFDestinationInterne and OForiginInterne, neither an
            // unambiguous match, same annex-drift family as OrdreFabricationMapper.SuiviDeZoneZone
            // (Story 4.3). Left at its CLR default (null) rather than guessing which one (deferred-work.md
            // § "story-4.4 mapper implementation", AC-FR19-4).
        };
    }
}
