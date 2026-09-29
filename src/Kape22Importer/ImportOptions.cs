using System;

namespace Kape22Importer;

// FR-12 configuration, bound from the "Import" section (AC-FR12-8, CC-7): every path, the polling
// interval, the initiating server and the retention window come from IConfiguration, never a literal
// in code. Defaults live in appsettings.json, except InitiatingServer, which is a per-deployment
// value supplied by the host environment (Story 3.3), and StabilityQuietPeriod, whose 10-second
// default is the property initializer (Story 6.2).
// Properties are declared in alphabetical order (CC-4).
public sealed class ImportOptions
{
    public const string SectionName = "Import";

    // Working sub-folder for archived successes, under the reception root. Dated sub-folders
    // <yyyy>/<MM> are created beneath it (AC-FR12-3).
    public string ArchiveFolder { get; set; } = string.Empty;

    // Working sub-folder for rejected Fichiers (AC-FR12-4).
    public string ErrorFolder { get; set; } = string.Empty;

    // Reception folder scanned each tick (D1).
    public string InboxPath { get; set; } = string.Empty;

    // L_D_LOG_COMMANDE.User / MQTTnetServices.Logs origin (D8). Since Story 6.1 the host passes the
    // "Import:InitiatingServer" key to the LSI journal (AscoLsiFichierJournal), which falls back to
    // Environment.MachineName; the importer itself no longer reads it.
    public string InitiatingServer { get; set; } = string.Empty;

    // Delay between two scans of the inbox.
    public TimeSpan PollingInterval { get; set; }

    // Working sub-folder a Fichier is moved to before it is read (AC-FR12-2).
    public string ProcessingFolder { get; set; } = string.Empty;

    // Files in ArchiveFolder / ErrorFolder older than this many days are purged; zero or less disables
    // the purge (AC-FR12-9, D13).
    public int RetentionDays { get; set; }

    // An inbox Fichier whose last write is more recent than this may still be copied in; it is left in
    // the inbox and retried next tick (AC-FR12-5). Defaults to 10 seconds when not configured.
    public TimeSpan StabilityQuietPeriod { get; set; } = TimeSpan.FromSeconds(10);
}
