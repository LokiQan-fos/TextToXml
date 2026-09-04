using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TextToXml;

// Fourth pipeline stage: assign each non-empty Ligne to a Bloc (Header/Detail/Footer) from the
// sections declared in the Descripteur and expectedMessageCount, check the Ligne count, and run the
// non-blocking Segment control (FR-3). Pure, no I/O, no mutable static state (CC-6).
internal static class BlockAssigner
{
    // Trailing empty or whitespace-only Lignes are dropped before the count; an empty Ligne in the
    // middle still counts. The first Ligne is the Header when a <header> section is declared, the last
    // is the Footer when a <footer> section is declared, and every Ligne in between is a Detail. When
    // expectedMessageCount is present the number of Detail Lignes must match it exactly, otherwise at
    // least one Detail Ligne is required; on any mismatch a single File-level WrongBlockCount error is
    // returned and no Champ is read. When segmentField and the matching *Marker attributes are set, the
    // Segment Champ of each Bloc is compared to its expected marker and every mismatch is added to
    // Warnings without changing Success.
    public static BlockAssignmentResult Assign(IReadOnlyList<string> lines, XElement descriptorRoot)
    {
        // Drop trailing empty or whitespace-only Lignes before counting (AC-FR3-7).
        int end = lines.Count;
        while (end > 0 && string.IsNullOrWhiteSpace(lines[end - 1]))
        {
            end--;
        }

        List<string> content = lines.Take(end).ToList();

        // Every non-empty Ligne is assigned to a Bloc; a Fichier with none cannot be, so report it
        // like any other count mismatch rather than failing further down.
        if (content.Count == 0)
        {
            return new BlockAssignmentResult
            {
                Error = WrongBlockCount("Nombre de Lignes incorrect : le Fichier ne contient aucune Ligne non vide."),
            };
        }

        bool hasHeader = descriptorRoot.Element("header") is not null;
        bool hasFooter = descriptorRoot.Element("footer") is not null;
        int headerCount = hasHeader ? 1 : 0;
        int footerCount = hasFooter ? 1 : 0;

        int? expectedMessageCount = ParseExpectedMessageCount(descriptorRoot);
        int detailCount = content.Count - headerCount - footerCount;

        bool countValid = expectedMessageCount is not null
            ? content.Count == headerCount + expectedMessageCount.Value + footerCount
            : detailCount >= 1;

        if (!countValid)
        {
            string message = expectedMessageCount is not null
                ? $"Nombre de Lignes incorrect : {headerCount + expectedMessageCount.Value + footerCount} "
                  + $"attendue(s), {content.Count} trouvée(s)."
                : $"Nombre de Lignes incorrect : au moins {headerCount + 1 + footerCount} attendue(s), "
                  + $"{content.Count} trouvée(s).";

            return new BlockAssignmentResult { Error = WrongBlockCount(message) };
        }

        Block[] blocks = AssignRoles(content.Count, hasHeader, hasFooter);
        IReadOnlyList<ConversionError> warnings = CheckSegments(content, blocks, descriptorRoot);

        return new BlockAssignmentResult { Blocks = blocks, Lines = content, Warnings = warnings };
    }

    private static Block[] AssignRoles(int count, bool hasHeader, bool hasFooter)
    {
        Block[] roles = new Block[count];
        for (int i = 0; i < count; i++)
        {
            roles[i] = Block.Detail;
        }

        if (hasHeader)
        {
            roles[0] = Block.Header;
        }

        if (hasFooter)
        {
            roles[count - 1] = Block.Footer;
        }

        return roles;
    }

    private static IReadOnlyList<ConversionError> CheckSegments(
        IReadOnlyList<string> lines,
        IReadOnlyList<Block> blocks,
        XElement root)
    {
        List<ConversionError> warnings = [];

        string? segmentField = (string?)root.Attribute("segmentField");
        if (string.IsNullOrEmpty(segmentField))
        {
            return warnings;
        }

        foreach ((Block bloc, string markerAttribute, string section) in DescriptorSections.All)
        {
            XElement? sectionElement = root.Element(section);
            string? marker = (string?)root.Attribute(markerAttribute);

            // An absent or empty marker attribute disables the control for this Bloc.
            if (sectionElement is null || string.IsNullOrEmpty(marker))
            {
                continue;
            }

            XElement? field = sectionElement.Elements("value")
                .FirstOrDefault(value => (string?)value.Attribute("Id") == segmentField);
            if (field is null)
            {
                continue;
            }

            // The Descripteur validator has already checked Position and Size are non-negative integers.
            int position = (int)field.Attribute("Position")!;
            int size = (int)field.Attribute("Size")!;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i] != bloc)
                {
                    continue;
                }

                string line = lines[i];

                // Skip the check when the Ligne does not carry the full Segment Champ; Story 1.5
                // reports the truncation as LineTooShort, so raising a mismatch here would double up.
                if (position + size > line.Length)
                {
                    continue;
                }

                // Compare against the trimmed slice so fixed-width padding is not read as a mismatch,
                // consistent with string normalization (AC-FR5-3).
                string rawValue = line.Substring(position, size).TrimEnd();
                if (string.Equals(rawValue, marker, StringComparison.Ordinal))
                {
                    continue;
                }

                warnings.Add(new ConversionError
                {
                    Block = bloc,
                    Code = ErrorCode.SegmentMismatch,
                    FieldId = segmentField,
                    LineNumber = i + 1,
                    Message =
                        $"La valeur Segment « {rawValue} » ne correspond pas au marqueur attendu « {marker} ».",
                    RawValue = rawValue,
                });
            }
        }

        return warnings;
    }

    private static int? ParseExpectedMessageCount(XElement root)
    {
        // The Descripteur validator has already checked a present expectedMessageCount is a positive integer.
        string? raw = (string?)root.Attribute("expectedMessageCount");
        return raw is null
            ? null
            : int.Parse(raw, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static ConversionError WrongBlockCount(string message)
    {
        return new ConversionError
        {
            Block = Block.File,
            Code = ErrorCode.WrongBlockCount,
            LineNumber = 0,
            Message = message,
        };
    }
}

// Outcome of BlockAssigner.Assign. On success Error is null, Blocks holds one entry per non-empty
// Ligne, in Ligne order, and Lines holds those same Lignes after trailing empty Lignes were dropped;
// on failure Error carries the single WrongBlockCount error and Blocks and Lines are empty.
// Warnings holds the SegmentMismatch entries and never blocks the conversion.
// Properties are declared in alphabetical order (CC-4).
internal sealed record BlockAssignmentResult
{
    public IReadOnlyList<Block> Blocks { get; init; } = [];

    public ConversionError? Error { get; init; }

    public IReadOnlyList<string> Lines { get; init; } = [];

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];
}
