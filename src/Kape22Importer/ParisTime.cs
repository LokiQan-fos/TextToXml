using System;

namespace Kape22Importer;

// The Europe/Paris zone, resolved once and shared by every importer type that interprets a derived
// timestamp in Paris local time (D4 for the Header Date, AC-FR9-3 for DateReception, AC-FR12-3 for the
// archive date folder): Kape22Mapper, Kape22Persister, InboxScanner and Kape22FichierProcessor.
// Lazy so the lookup happens on first use, not in an unrelated type's static initializer, and it tries
// the Windows id after the IANA one so a host without the IANA database still resolves the zone. A
// host with neither fails here with a clear message rather than a TypeInitializationException.
internal static class ParisTime
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(Resolve);

    public static TimeZoneInfo Instance => Zone.Value;

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
