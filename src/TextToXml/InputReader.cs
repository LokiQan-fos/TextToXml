using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TextToXml;

// Second pipeline stage: decode the raw Windows-1252 bytes with a strict decoder and split the
// Fichier into Lignes, tolerating LF and CR LF endings (FR-2). Returns either the Lignes or the
// single File-level error that stops the conversion (EmptyFile, UndecodableInput). Pure, no I/O (CC-6).
internal static class InputReader
{
    // Shared wording for both the explicit undefined-byte scan and the defensive decoder fallback.
    private const string UndecodableMessage =
        "Le Fichier contient un octet qui ne peut pas être décodé en Windows-1252.";

    // Wording for a C0 control byte that decodes cleanly but cannot appear in an XML document.
    private const string ControlCharacterMessage =
        "Le Fichier contient un caractère de contrôle interdit dans un document XML.";

    private static readonly Encoding Windows1252;

    static InputReader()
    {
        // The library registers the code-pages provider itself, so a consumer needs no setup (AR-10).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Strict decoder: an unassigned Windows-1252 byte raises DecoderFallbackException (D19).
        Windows1252 = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    public static InputReadResult Read(ReadOnlySpan<byte> input)
    {
        if (input.Length == 0)
        {
            return Failure(ErrorCode.EmptyFile, "Le Fichier est vide.");
        }

        // The .NET Windows-1252 decoder silently maps the five undefined byte positions to their
        // C1 control characters instead of failing, so the strict rejection required by D19 is
        // enforced here explicitly. A C0 control byte decodes cleanly but is not a legal XML 1.0
        // character, so it is rejected here rather than throwing later while the normalized XML is
        // written. Tab, line feed and carriage return are the only control characters XML 1.0 allows.
        foreach (byte value in input)
        {
            if (value is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
            {
                return Failure(ErrorCode.UndecodableInput, UndecodableMessage);
            }

            if (value < 0x20 && value is not 0x09 and not 0x0A and not 0x0D)
            {
                return Failure(ErrorCode.UndecodableInput, ControlCharacterMessage);
            }
        }

        string decoded;
        try
        {
            decoded = Windows1252.GetString(input);
        }
        catch (DecoderFallbackException)
        {
            // Defensive fallback: the scan above already rejects every undefined Windows-1252 byte,
            // but the strict decoder stays configured so a future framework change cannot let a bad
            // byte through silently.
            return Failure(ErrorCode.UndecodableInput, UndecodableMessage);
        }

        List<string> lines = SplitIntoLignes(decoded);

        // A Fichier that carries nothing but spaces and line breaks is treated as empty (AC-FR2-2).
        if (lines.Count == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            return Failure(
                ErrorCode.EmptyFile,
                "Le Fichier ne contient que des espaces et des sauts de ligne.");
        }

        return new InputReadResult { Lines = lines };
    }

    private static List<string> SplitIntoLignes(string decoded)
    {
        string[] segments = decoded.Split('\n');
        List<string> lines = new(segments.Length);

        foreach (string segment in segments)
        {
            // Remove the residual CR left by a CR LF ending before analysis (AC-FR2-6).
            lines.Add(segment.EndsWith('\r') ? segment[..^1] : segment);
        }

        // A single terminating LF must not add an empty Ligne (AC-FR2-7); a missing final LF still
        // keeps the last Ligne, since Split already yields it (AC-FR2-5).
        if (decoded.EndsWith('\n') && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static InputReadResult Failure(ErrorCode code, string message)
    {
        return new InputReadResult
        {
            Error = new ConversionError
            {
                Block = Block.File,
                Code = code,
                LineNumber = 0,
                Message = message,
            },
        };
    }
}

// Outcome of InputReader.Read. On success Error is null and Lines holds the Fichier split into
// Lignes; on failure Error carries the single File-level error and Lines is empty.
// Properties are declared in alphabetical order (CC-4).
internal sealed record InputReadResult
{
    public ConversionError? Error { get; init; }

    public IReadOnlyList<string> Lines { get; init; } = [];
}
