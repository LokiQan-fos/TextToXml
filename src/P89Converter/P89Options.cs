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

    // Delay between two scans of the source folder.
    public TimeSpan PollingInterval { get; set; }

    // Folder scanned for LP89_* Fichiers each tick.
    public string SourcePath { get; set; } = string.Empty;

    // Folder the normalized XML <name>_<yyyyMMddHHmmss>.xml is written to.
    public string XmlPath { get; set; } = string.Empty;
}
