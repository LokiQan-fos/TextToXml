using System;
using System.Collections.Generic;

namespace Kape22Importer.Persistence;

// Story 4.10 (B-5): the exclusive upper bound (10^(p-s)) of every narrow DECIMAL(p,s) downstream column
// DecimalScale.Apply scales (Story 4.3-bis) - the 17 distinct column names behind the 21 call sites
// (four Tolerance* names - ToleranceMaxEpaisseur, ToleranceMaxSection, ToleranceMinEpaisseur,
// ToleranceMinSection - are shared between L_D_ORDRE_FABRICATION and L_D_SECTIONCHARGE_LINGOT, at the
// same DECIMAL(p,s) in both; ToleranceMaxLongueur/ToleranceMinLongueur exist only in
// L_D_ORDRE_FABRICATION). Taken once from scripts/schema/01-ascolsi-tables.sql, the same
// shared-by-column-name discipline as DownstreamColumnLengths, parity-locked by
// DownstreamColumnMagnitudesParityTests. Kape22Persister checks a mapped entity's scaled value against
// this before SaveChanges, so an out-of-gabarit raw KAPE22 int surfaces as a diagnosed ConversionError
// instead of an undiagnosed SQL overflow.
public static class DownstreamColumnMagnitudes
{
    // Keyed by column name, in case-insensitive dictionary order (CC-4), matching DownstreamColumnLengths.
    public static readonly IReadOnlyDictionary<string, decimal> MaxAbsoluteValues =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["ChutagePied"] = 10m,
            ["ChutageTete"] = 10m,
            ["DiametreProduit"] = 1000m,
            ["Epaisseur"] = 1000m,
            ["EpaisseurEnLaminage"] = 1000m,
            ["H2Coulee"] = 10m,
            ["LongueurCD"] = 100m,
            ["LongueurMoyenne"] = 100m,
            ["PoidsDemiProduitUnitaire"] = 10000m,
            ["PoidsPrevuDemiProduit"] = 1000m,
            ["SectionLaminage"] = 1000m,
            ["ToleranceMaxEpaisseur"] = 10m,
            ["ToleranceMaxLongueur"] = 10000m,
            ["ToleranceMaxSection"] = 10m,
            ["ToleranceMinEpaisseur"] = 10m,
            ["ToleranceMinLongueur"] = 10000m,
            ["ToleranceMinSection"] = 10m,
        };
}
