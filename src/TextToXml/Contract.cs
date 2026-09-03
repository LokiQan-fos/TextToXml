using System.Collections.Generic;

namespace TextToXml;

// The public contract of the library, frozen for v1 by PRD section 4.1 and the glossary in section 3.
// Filled in by later stories; declared now so the Story 1.2 descriptor-validation tests compile and run red.

// Section of a Fichier an error or warning belongs to. File means the whole Fichier (LineNumber 0).
public enum Block
{
    Header,
    Detail,
    Footer,
    File,
}

// Closed set of error and warning codes. Declaration order mirrors the PRD glossary (section 3, CC-5);
// CC-4 alphabetical ordering governs properties, not enum members, and grouping reads better here.
public enum ErrorCode
{
    // Blocking structure errors (Step 1).
    EmptyFile,
    UndecodableInput,
    LayoutInvalid,
    WrongBlockCount,
    LineTooShort,

    // Blocking typing errors (Step 1).
    InvalidInteger,
    InvalidDecimal,
    InvalidDate,

    // Blocking errors (Step 2).
    RequiredFieldMissing,
    PersistenceError,

    // Non-blocking consistency warnings.
    SegmentMismatch,
    InterBlockMismatch,
    FileNameMismatch,
}

// A single blocking error or non-blocking warning. Serializable as-is by System.Text.Json (AC-FR6-7).
// Properties are declared in alphabetical order (CC-4).
public sealed record ConversionError
{
    public Block Block { get; init; }

    public ErrorCode Code { get; init; }

    public string? Column { get; init; }

    public string? FieldId { get; init; }

    public int LineNumber { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? RawValue { get; init; }
}

// Result of Converter.Convert. Success is true exactly when Errors is empty; Xml is null on failure.
// Properties are declared in alphabetical order (CC-4).
public sealed record ConversionResult
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public bool Success => Errors.Count == 0;

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];

    public string? Xml { get; init; }
}
