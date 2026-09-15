using System.Collections.Generic;
using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4 (FR-19): explicit mapper from L_D_KAPE22 (Epic 2) plus the 7 already-mapped
// L_D_SECTIONCHARGE_* entities (this same story) to the L_D_CONSIGNES rows they own, no reflective
// property lookup and no database access (AD-2, AC-FR19-1) - a pure function of its inputs. A section
// produces a Consignes row iff its own mapper produced a row (the same per-OF applicability rule,
// AC-FR19-2, already applied by that mapper) - no separate applicability check here. Each row's
// CodeOperation is copied verbatim from its owning section's own CodeOperation, the explicit scalar FK
// to that section AD-7 requires (never an EF navigation property, AC-FR19-3).
public static class ConsignesMapper
{
    public static List<L_D_CONSIGNES> Map(
        L_D_KAPE22 source,
        L_D_SECTIONCHARGE_CHUTAGE? chutage,
        L_D_SECTIONCHARGE_DECOUPE? decoupe,
        L_D_SECTIONCHARGE_LINGOT? lingot,
        L_D_SECTIONCHARGE_PITS? pits,
        L_D_SECTIONCHARGE_POIDSMETRIQUE? poidsMetrique,
        L_D_SECTIONCHARGE_REFROIDISSOIRS? refroidissoirs,
        L_D_SECTIONCHARGE_SVT? svt)
    {
        List<L_D_CONSIGNES> consignes = [];

        if (chutage is not null)
        {
            consignes.Add(Build(source.OF, chutage.CodeOperation, source.CodeConsigneChutage));
        }

        if (decoupe is not null)
        {
            consignes.Add(Build(source.OF, decoupe.CodeOperation, source.CodeConsigneDecoupe));
        }

        if (lingot is not null)
        {
            consignes.Add(Build(source.OF, lingot.CodeOperation, source.CodeConsigneLingot));
        }

        if (pits is not null)
        {
            consignes.Add(Build(source.OF, pits.CodeOperation, source.CodeConsignePits));
        }

        if (poidsMetrique is not null)
        {
            consignes.Add(Build(source.OF, poidsMetrique.CodeOperation, source.CodeConsignePoidMetrique));
        }

        if (refroidissoirs is not null)
        {
            consignes.Add(Build(source.OF, refroidissoirs.CodeOperation, source.CodeConsigneRefroidissoir));
        }

        if (svt is not null)
        {
            consignes.Add(Build(source.OF, svt.CodeOperation, source.CodeConsigneSVT));
        }

        return consignes;
    }

    // A "sourcée" string column (CodeConsigne) whose L_D_KAPE22 field is itself null falls back to
    // string.Empty rather than propagating null onto this NOT NULL target column - same posture as
    // OrdreFabricationMapper's equivalent fallback (Story 4.3).
    private static L_D_CONSIGNES Build(string of, string codeOperation, string? codeConsigne) => new()
    {
        CodeConsigne = codeConsigne ?? string.Empty,
        CodeOperation = codeOperation,

        // ConsigneGPAO: règle - false for every row this mapper produces (the real, non-mirror consigne);
        // the legacy mirror-row duplication (AddOrModifyConsigne, OrdreDeFabricationManager.cs:1362-1435,
        // creating a second true-flagged row per consigne when gpao=true) is not reproduced here -
        // producing exactly the one documented row per section satisfies AC-FR19-3, same posture as
        // CouleeMapper.Externe (annex flags this unconfirmed, inference by exclusion, Story 4.3).
        ConsigneGPAO = false,

        OF = of,

        // LibelleConsigne, SizeCodeConsigne, TypeConsigne: assumed, unverified - à_clarifier per the
        // Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_CONSIGNES); no single column-to-column
        // rule found in the legacy read, left at their CLR default (deferred-work.md § "story-4.2 mapping
        // annex", AC-FR19-4). Leaving TypeConsigne at 0 for every section means the table's own natural
        // key (OF, CodeOperation, TypeConsigne, ConsigneGPAO) only stays unique across the 7 sections of
        // one OF as long as their CodeOperation values differ, which every reference Fichier read so far
        // satisfies (deferred-work.md § "story-4.4 mapper implementation").
    };
}
