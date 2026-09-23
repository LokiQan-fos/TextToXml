using System;
using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.3 (FR-18): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_ORDRE_FABRICATION (Story 4.1), one
// property per line, no reflective property lookup and no database access (AD-2, AC-FR18-2) - a pure
// function of its two inputs. Every assignment below is sourced or ruled by the Story 4.2 annex
// (annexe-mapping-dispatch-epic4.md § L_D_ORDRE_FABRICATION); nothing here invents a rule the annex does
// not document (AC-FR18-1).
//
// A "sourcée" string column whose L_D_KAPE22 field is itself null falls back to string.Empty rather than
// propagating null onto these NOT NULL target columns - assumed, unverified, no null case observed for
// these fields in the annex or the FR-7 production parity read (see deferred-work.md). The equivalent
// `?? 0` fallback on the int/decimal columns (DiametreProduit, Epaisseur, LongueurCD, NombreDemiProduit,
// the six Tolerance*) is defensive only and provably unreachable today: Kape22Mapper.DefaultForNonNullable
// already zero-fills every blank int Champ upstream (no NULL observed in 17710 production rows).
//
// Story 4.3-bis: DiametreProduit, Epaisseur, LongueurCD, PoidsDemiProduitUnitaire, PoidsPrevuDemiProduit
// and the six Tolerance* columns are narrow DECIMAL columns in AscoLSI (Story 4.2-bis annex Scale) -
// each is wrapped in DecimalScale.Apply with its literal annex scale instead of the raw widened int.
public static class OrdreFabricationMapper
{
    // timeProvider defaults to the system clock in production; tests inject a fixed one (AR-12) for the
    // DateMaj/DateReception audit timestamps, the same TimeProvider convention as DerivedFields,
    // InboxScanner, Kape22FichierProcessor and Kape22Persister (Paris local time, ParisTime.Zone).
    public static L_D_ORDRE_FABRICATION Map(L_D_KAPE22 source, TimeProvider? timeProvider = null)
    {
        DateTime importTimestamp = TimeZoneInfo.ConvertTimeFromUtc(
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, ParisTime.Zone);

        return new L_D_ORDRE_FABRICATION
        {
            AcompteSolde = source.AcompteSolde ?? string.Empty,
            ClasseDeChute = source.ClasseDeChute ?? string.Empty,
            Client = source.Client,
            CodeDemiProduit = source.CodeDemiProduit ?? string.Empty,
            Coulee = source.Coulee,
            DateMaj = importTimestamp,
            DateReception = importTimestamp,
            DiametreProduit = DecimalScale.Apply(source.DiametreProduit ?? 0, 1),
            Epaisseur = DecimalScale.Apply(source.Epaisseur ?? 0, 1),
            Indice = source.Indice,
            LongueurCD = DecimalScale.Apply(source.LongueurCD ?? 0, 3),
            MarqueCommerciale = source.MarqueCommerciale ?? string.Empty,
            NombreDemiProduit = source.NombreDemiProduit ?? 0,
            Nuance = source.Nuance,
            NumeroFichier = source.NumeroFichier,
            NumeroMontage = source.NumeroMontage ?? string.Empty,
            OF = DownstreamOf.Pad(source.OF),
            PoidsDemiProduitUnitaire = DecimalScale.Apply(source.PoidsDemiProduitUnitaire, 3),
            PoidsPrevuDemiProduit = DecimalScale.Apply(source.PoidsPrevuDemiProduit, 3),
            ProfilProduit = source.ProfilProduit ?? string.Empty,
            ToleranceMaxEpaisseur = DecimalScale.Apply(source.ToleranceMaxEpaisseur ?? 0, 1),
            ToleranceMaxLongueur = DecimalScale.Apply(source.ToleranceMaxLongueur ?? 0, 0),
            ToleranceMaxSection = DecimalScale.Apply(source.ToleranceMaxSection ?? 0, 1),
            ToleranceMinEpaisseur = DecimalScale.Apply(source.ToleranceMinEpaisseur ?? 0, 1),
            ToleranceMinLongueur = DecimalScale.Apply(source.ToleranceMinLongueur ?? 0, 0),
            ToleranceMinSection = DecimalScale.Apply(source.ToleranceMinSection ?? 0, 1),
            Type = source.Type,

            // Etat, NombreLingotsWagon1Four1, NombreLingotsWagon1Four2, NombreLingotsWagon2Four1,
            // NombreLingotsWagon2Four2, OFOrigine, PoidsPesee, SensLaminage, SensLaminageGPAO: assumed,
            // unverified - no source or initialisation rule found in the legacy read for a P60 dispatch
            // (annexe-mapping-dispatch-epic4.md § L_D_ORDRE_FABRICATION, see deferred-work.md). Left at
            // their CLR default (0 / null) rather than a guessed value; does not block the rest of the
            // mapper (AC-FR18-4).

            // SuiviDeZoneZone: assumed, unverified - the Story 4.2 annex named a KAPE22.SuiviDeZoneZone
            // field that does not exist on L_D_KAPE22 (annex error, corrected at Story 4.3; see
            // deferred-work.md). Left at its CLR default (null) rather than guessing a substitute field.

            // DateDebut, DateDebutLaminage, DateEVC, DateFin, DateFinLaminage, TemperatureScarfing,
            // TemperatureT03, TemperatureT07: NULL at dispatch by rule - each is positioned later by a
            // distinct, non-P60 event (annex).
        };
    }
}
