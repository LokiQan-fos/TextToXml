using System;
using System.Collections.Generic;

namespace Kape22Importer.Persistence;

// Story 4.1: the character max_length of every bounded string column across the 10 downstream dispatch
// tables (L_D_ORDRE_FABRICATION, L_D_COULEE, L_D_CONSIGNES, the 7 L_D_SECTIONCHARGE_*), taken once from
// scripts/schema/01-ascolsi-tables.sql (generated from AFV004-LSI sys.columns, 2026-09-14). One shared
// map keyed by column name: no column name that repeats across two of the 10 tables (OF, CodeOperation,
// RangOperation, Nuance) carries a different length in the two tables, so a single map is safe.
// AscoLsiDbContext applies these with HasMaxLength, restricted per entity to the columns it actually
// has. NVARCHAR(MAX) columns (for example L_D_ORDRE_FABRICATION.TemperatureScarfing,
// L_D_COULEE.Observations) carry no entry. DownstreamColumnLengthsParityTests locks this map to
// scripts/schema/.
public static class DownstreamColumnLengths
{
    // Keyed by column name, in case-insensitive dictionary order (CC-4). The comparer is
    // case-insensitive, matching Kape22ColumnLengths.
    public static readonly IReadOnlyDictionary<string, int> MaxLengths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AcompteSolde"] = 1,
            ["ClasseDeChute"] = 4,
            ["Client"] = 13,
            ["CodeConsigne"] = 18,
            ["CodeDemiProduit"] = 4,
            ["CodeLivraison"] = 10,
            ["CodeOperation"] = 3,
            ["Coulee"] = 6,
            ["Destination"] = 1,
            ["Enregistrement"] = 3,
            ["GazScarfing"] = 3,
            ["IdCoulee"] = 6,
            ["LongueurScarfingPied"] = 2,
            ["LongueurScarfingTete"] = 2,
            ["MarqueCommerciale"] = 9,
            ["MiseAuMille"] = 4,
            ["Nuance"] = 7,
            ["NuanceMarquage"] = 6,
            ["NumeroFichier"] = 8,
            ["NumeroMontage"] = 3,
            ["Observation2"] = 100,
            ["OF"] = 12,
            ["OFDestination"] = 12,
            ["OFInterne"] = 12,
            ["OFOrigin"] = 12,
            ["OFOrigine"] = 12,
            ["OperateurCoulee"] = 30,
            ["OperateurDegazeur"] = 10,
            ["OperateurDemoulage"] = 10,
            ["OutilDeDecoupe"] = 1,
            ["OxygeneInferieur"] = 3,
            ["OxygeneLatent"] = 3,
            ["OxygeneSuperieur"] = 3,
            ["ProfileLamine"] = 1,
            ["ProfilProduit"] = 3,
            ["ProgrammeSMQ"] = 3,
            ["RangOperation"] = 3,
            ["RefroidissementBloom"] = 8,
            ["ResponsableTraitement"] = 30,
            ["SensLaminage"] = 1,
            ["SensLaminageGPAO"] = 1,
            ["SuiviDeZoneZone"] = 50,
            ["Type"] = 1,
            ["TypeLingot1"] = 4,
            ["TypeLingot2"] = 4,
            ["VitesseV1"] = 4,
            ["VitesseV2"] = 4,
            ["VitesseV3"] = 4,
        };
}
