using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TextToXml;

// Fifth pipeline stage: for a Descripteur with format="Fixed", check that every Ligne covers the
// starting Position of each of its Champs (FR-4). The last declared Champ of a Bloc is exempt: a
// trailing Filler / Reserve may be truncated or entirely absent at the end of the Ligne, the real
// NOT NULL obligation being enforced later in Step 2. A Ligne that is too short yields a single
// LineTooShort error citing the first missing Position; a Ligne longer than its last Champ is fine,
// the surplus is ignored (D5). Pure, no I/O, no mutable static state (CC-6).
internal static class LineLengthChecker
{
    // Lines and blocks are aligned one-to-one, in Ligne order, as produced by BlockAssigner. The
    // returned list carries at most one LineTooShort per Ligne, in Ligne order.
    public static IReadOnlyList<ConversionError> Check(
        IReadOnlyList<string> lines,
        IReadOnlyList<Block> blocks,
        XElement descriptorRoot)
    {
        List<ConversionError> errors = [];

        for (int i = 0; i < lines.Count; i++)
        {
            ConversionError? error = CheckLine(lines[i], blocks[i], i + 1, descriptorRoot);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return errors;
    }

    private static ConversionError? CheckLine(string line, Block bloc, int lineNumber, XElement root)
    {
        string? sectionName = DescriptorSections.For(bloc);
        if (sectionName is null)
        {
            return null;
        }

        XElement? section = root.Element(sectionName);
        if (section is null)
        {
            return null;
        }

        List<XElement> champs = section.Elements("value").ToList();

        // The Champ declared last in the section is exempt from the check: a trailing Filler / Reserve
        // may be missing at the end of the Ligne (AC-FR4-1). Descripteurs declare their Champs in
        // Position order, so the last-declared Champ is also the physically last one on the Ligne (R-4).
        for (int index = 0; index < champs.Count - 1; index++)
        {
            // The Descripteur validator has already checked Position is a non-negative integer.
            int position = int.Parse(
                (string)champs[index].Attribute("Position")!,
                NumberStyles.None,
                CultureInfo.InvariantCulture);

            // A Ligne covers a Champ when at least its first character is present (AC-FR4-3, AC-FR4-4).
            if (line.Length > position)
            {
                continue;
            }

            string id = (string)champs[index].Attribute("Id")!;

            return new ConversionError
            {
                Block = bloc,
                Code = ErrorCode.LineTooShort,
                FieldId = id,
                LineNumber = lineNumber,
                Message =
                    $"Ligne trop courte : le Champ « {id} » commence en Position {position} "
                    + $"mais la Ligne ne fait que {line.Length} caractère(s).",
            };
        }

        return null;
    }
}
