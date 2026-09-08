using System.Collections.Generic;
using TextToXml;

namespace Kape22Importer;

// The Story 3.1 <-> Story 3.2 seam. InboxScanner hands the processing/ copy of a Fichier to an
// IFichierProcessor and gets this back: the normalized XML to archive on success (AC-FR12-3), or the
// Errors to write next to the Fichier in error/ as <name>.errors.json (AC-FR12-4). Success is derived,
// like ConversionResult / MapResult / ImportResult (CC-5).
// Properties are declared in alphabetical order (CC-4).
public sealed record FichierProcessingResult
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public string? NormalizedXml { get; init; }

    public bool Success => Errors.Count == 0;

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];
}
