using System;
using System.Collections.Generic;

namespace Kape22Importer.Persistence;

// The (precision, scale) of every decimal column across the downstream dispatch tables
// (L_D_ORDRE_FABRICATION, L_D_COULEE, the 7 L_D_SECTIONCHARGE_*), taken from the real schema the same
// way as DownstreamColumnLengths. None of these is configured anywhere else in AscoLsiDbContext, so EF
// Core falls back to its default decimal(18,2) convention - silently rounding any scale-3 column
// (PoidsDemiProduitUnitaire, PoidsPrevuDemiProduit, LongueurCD, LongueurMoyenne, DensiteCoulee) to 2
// decimals in the outbound SqlParameter, before the value ever reaches the real, wider column. Found via
// Kape22ProductionDataParityTests once its production lookup was fixed for the OF zero-padding issue.
// One shared map keyed by column name: no repeated name (the four Tolerance* columns shared between
// L_D_ORDRE_FABRICATION and L_D_SECTIONCHARGE_LINGOT) carries a different precision/scale in the two
// tables.
public static class DownstreamColumnPrecisions
{
    // Keyed by column name, in case-insensitive dictionary order (CC-4), matching DownstreamColumnLengths.
    public static readonly IReadOnlyDictionary<string, (int Precision, int Scale)> Precisions =
        new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["ChutagePied"] = (3, 2),
            ["ChutageTete"] = (3, 2),
            ["DensiteCoulee"] = (5, 3),
            ["DiametreProduit"] = (4, 1),
            ["Epaisseur"] = (4, 1),
            ["EpaisseurEnLaminage"] = (4, 1),
            ["H2Coulee"] = (2, 1),
            ["Hydrogene"] = (3, 2),
            ["LongueurCD"] = (5, 3),
            ["LongueurMoyenne"] = (5, 3),
            ["PoidsDemiProduitUnitaire"] = (7, 3),
            ["PoidsPrevuDemiProduit"] = (6, 3),
            ["PriseDeFerEpaisseur"] = (4, 1),
            ["PriseDeFerEpaisseurGPAO"] = (4, 1),
            ["PriseDeFerHauteur"] = (4, 1),
            ["PriseDeFerHauteurGPAO"] = (4, 1),
            ["SectionLaminage"] = (4, 1),
            ["ToleranceMaxEpaisseur"] = (2, 1),
            ["ToleranceMaxLongueur"] = (4, 0),
            ["ToleranceMaxSection"] = (2, 1),
            ["ToleranceMinEpaisseur"] = (2, 1),
            ["ToleranceMinLongueur"] = (4, 0),
            ["ToleranceMinSection"] = (2, 1),
        };
}
