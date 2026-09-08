using System.Collections.Generic;
using TextToXml;

namespace Kape22Importer;

// End-to-end outcome for one Fichier (PRD glossary section 3). Like ConversionResult and MapResult,
// Success is derived: it is true exactly when Errors is empty. InsertedId is the L_D_KAPE22 identity
// on a fresh insert and null otherwise (rejection, SQL failure, or guard hit). Errors and Warnings
// carry the same ConversionError values the rest of the pipeline uses. NormalizedXml is the Step 1
// output, non-null as soon as Converter.Convert succeeds (Story 3.2, AC-FR13-3). XmlArchivePath is
// filled by the Epic 3 orchestrator once the normalized XML is written next to the archived Fichier.
// The anti-duplicate skip is the one case with Success == true, empty Errors, and InsertedId == null.
// Properties are declared in alphabetical order (CC-4).
public sealed record ImportResult
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public int? InsertedId { get; init; }

    public string? NormalizedXml { get; init; }

    public bool Success => Errors.Count == 0;

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];

    public string? XmlArchivePath { get; init; }
}
