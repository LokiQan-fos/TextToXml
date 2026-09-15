using System;
using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.3 (FR-18): explicit mapper from L_D_KAPE22 (Epic 2) to L_D_COULEE (Story 4.1), no reflective
// property lookup and no database access (AD-2, AC-FR18-2/AC-FR18-3) - a pure function of its two
// inputs. The Story 4.2 annex (annexe-mapping-dispatch-epic4.md § L_D_COULEE) finds no P60 dispatch
// equivalent for almost the entire table (a different legacy system sources Coulee.xml); only the seven
// columns below are sourced or ruled, everything else stays at its CLR default (null, every remaining
// column is nullable).
public static class CouleeMapper
{
    // timeProvider defaults to the system clock in production; tests inject a fixed one (AR-12) for the
    // DateReception/DerniereModif audit timestamps, the same TimeProvider convention as
    // OrdreFabricationMapper and every other TimeProvider consumer in Kape22Importer (Paris local time,
    // ParisTime.Zone).
    public static L_D_COULEE Map(L_D_KAPE22 source, TimeProvider? timeProvider = null)
    {
        DateTime importTimestamp = TimeZoneInfo.ConvertTimeFromUtc(
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime, ParisTime.Zone);

        return new L_D_COULEE
        {
            DateReception = importTimestamp,
            DerniereModif = importTimestamp,

            // EtatReception, NbLingotRestantARefroidir: assumed, unverified - both are NOT NULL columns
            // the annex flags à_clarifier with no initial value found in the legacy read for a P60
            // dispatch (annexe-mapping-dispatch-epic4.md § L_D_COULEE, see deferred-work.md). Left at
            // their CLR default 0 rather than a guessed enum state or count; does not block the rest of
            // the mapper (AC-FR18-4).
            EtatReception = 0,

            // Externe is always false for a Coulee created by a KAPE22 dispatch - true is only reached
            // through legacy administrative scenarios the P60 flow never calls (CreateDefaultFroid /
            // CreateFakeBUL, annex evidence).
            Externe = false,

            // IdCoulee is the OF's Coulee (KAPE22.Coulee), the annex's one renamed field for this table.
            IdCoulee = source.Coulee,

            NbLingotRestantARefroidir = 0,

            Nuance = source.Nuance,

            // Every other column (CouleeFroide included) is à_clarifier with no P60 dispatch equivalent
            // found in the legacy read - assumed, unverified, left at its CLR default null rather than a
            // guessed value (annexe-mapping-dispatch-epic4.md § L_D_COULEE, see deferred-work.md).
        };
    }
}
