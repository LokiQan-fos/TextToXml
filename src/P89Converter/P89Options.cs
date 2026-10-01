using System;

namespace P89Converter;

// Story 5.1 configuration, bound from the "P89" section of the worker's JSON file (CC-7): every folder
// and the polling interval come from configuration, never a literal in code.
// Properties are declared in alphabetical order (CC-4).
public sealed class P89Options
{
    public const string SectionName = "P89";

    // Folder a converted Fichier is moved to, under <name>_<yyyyMMddHHmmss>.
    public string DonePath { get; set; } = string.Empty;

    // Folder a rejected Fichier is moved to, under <name>_<yyyyMMddHHmmss>.
    public string ErrorPath { get; set; } = string.Empty;

    // AC-FR25-8 (Story 6.7): a Fichier deferred this many consecutive ticks is reported once as Frozen and
    // no longer processed until the worker restarts. Defaults to 10; zero or less turns the cap off at
    // library level (GpaoConvertP89 refuses a value below 1).
    public int MaxAttempts { get; set; } = 10;

    // Delay between two scans of the source folder.
    public TimeSpan PollingInterval { get; set; }

    // Folder scanned for LP89_* Fichiers each tick.
    public string SourcePath { get; set; } = string.Empty;

    // A source Fichier whose last write is more recent than this may still be copied in; it is left in the
    // source folder and retried next tick (AC-FR22-9). Defaults to 10 seconds when not configured; zero or
    // less disables the gate, future timestamps included.
    public TimeSpan StabilityQuietPeriod { get; set; } = TimeSpan.FromSeconds(10);

    // Folder the normalized XML <name>_<yyyyMMddHHmmss>.xml is written to.
    public string XmlPath { get; set; } = string.Empty;
}
