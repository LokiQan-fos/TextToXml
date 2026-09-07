using System;
using System.Collections.Generic;

namespace Kape22Importer.Persistence;

// Story 2.5 (AC-FR8-2, risk R-6): the character max_length of every bounded string column of
// L_D_KAPE22, taken once from Annexe C / scripts/schema/01-ascolsi-tables.sql (generated from
// AFV004-LSI) and carried here as constants. The startup compatibility check reads these through the
// EF model (AscoLsiDbContext applies them with HasMaxLength); nothing queries sys.columns at runtime.
// NumeroFichier is NVARCHAR(MAX) and carries no entry. Kape22ColumnLengthsParityTests locks this map
// to scripts/schema/.
public static class Kape22ColumnLengths
{
    // Keyed by L_D_KAPE22 column name, in case-insensitive dictionary order. The comparer is
    // case-insensitive too, matching how Kape22Mapper resolves column names and how the EF model is
    // queried elsewhere.
    public static readonly IReadOnlyDictionary<string, int> MaxLengths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AcompteSolde"] = 1,
            ["ClasseDeChute"] = 4,
            ["Client"] = 13,
            ["CodeConsigneChutage"] = 12,
            ["CodeConsigneDecoupe"] = 12,
            ["CodeConsigneLingot"] = 12,
            ["CodeConsignePits"] = 12,
            ["CodeConsignePoidMetrique"] = 12,
            ["CodeConsigneRefroidissoir"] = 12,
            ["CodeConsigneSVT"] = 12,
            ["CodeDemiProduit"] = 4,
            ["CodeOpeChutage"] = 3,
            ["CodeOpeDecoupe"] = 3,
            ["CodeOpeLingot"] = 3,
            ["CodeOpePits"] = 3,
            ["CodeOpePoidMetrique"] = 3,
            ["CodeOpeRefroidissoir"] = 3,
            ["CodeOpeSVT"] = 3,
            ["Coulee"] = 6,
            ["Destination"] = 1,
            ["GazScarfing"] = 3,
            ["LibelleConsigneChutage"] = 18,
            ["LibelleConsigneDecoupe"] = 18,
            ["LibelleConsigneLingot"] = 18,
            ["LibelleConsignePits"] = 18,
            ["LibelleConsignePoidMetrique"] = 18,
            ["LibelleConsigneRefroidissoir"] = 18,
            ["LibelleConsigneSVT"] = 18,
            ["LongueurScarfingPied"] = 2,
            ["LongueurScarfingTete"] = 2,
            ["MarqueCommerciale"] = 9,
            ["MiseAuMille"] = 4,
            ["Nuance"] = 7,
            ["NuanceMarquage"] = 6,
            ["NumeroMontage"] = 3,
            ["OF"] = 12,
            ["OFDestination"] = 12,
            ["OFDestinationInterne"] = 12,
            ["OFOrigin"] = 12,
            ["OForiginInterne"] = 12,
            ["OutilDecoupe"] = 1,
            ["OxygeneInferieur"] = 3,
            ["OxygeneLatent"] = 3,
            ["OxygeneSuperieur"] = 3,
            ["ProfileLamine"] = 1,
            ["ProfilProduit"] = 3,
            ["RangOpeChutage"] = 3,
            ["RangOpeDecoupe"] = 3,
            ["RangOpeLingot"] = 3,
            ["RangOpePits"] = 3,
            ["RangOpePoidMetrique"] = 3,
            ["RangOpeRefroidissoir"] = 3,
            ["RangOpeSVT"] = 3,
            ["RefroidissementBloom"] = 8,
            ["Type"] = 1,
            ["VitesseV1"] = 2,
            ["VitesseV2"] = 2,
            ["VitesseV3"] = 2,
        };
}
