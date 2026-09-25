using System;

namespace AscoLsiJournal;

// The Europe/Paris zone the LSI journal Date is written in, resolved once. It tries the Windows id after
// the IANA one so a host without the IANA database still resolves the zone, and fails with a clear
// message on a host with neither.
internal static class ParisTime
{
    private static readonly Lazy<TimeZoneInfo> Cached = new(Resolve);

    public static TimeZoneInfo Zone => Cached.Value;

    private static TimeZoneInfo Resolve()
    {
        foreach (string id in new[] { "Europe/Paris", "Romance Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone))
            {
                return zone;
            }
        }

        throw new TimeZoneNotFoundException(
            "Neither 'Europe/Paris' nor 'Romance Standard Time' is available on this host.");
    }
}
