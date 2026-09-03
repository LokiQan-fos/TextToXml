using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TextToXml;

// Sixth and last pipeline stage: extract every Champ from its Ligne, normalize its raw value into the
// canonical value dictated by the Descripteur datatype, and - only when no Error is collected - emit
// the deterministic, deserializable normalized XML (FR-5). The document root is <file>, with one child
// element per Bloc present (<header>, <message>, <footer>), and under each a child named after the
// Champ Id carrying its canonical value. Pure, no I/O, no mutable static state (CC-6).
internal static class NormalizedXmlBuilder
{
    // The Bloc a Ligne was assigned to selects both the Descripteur section that declares its Champs
    // and the element name used for it in the normalized XML.
    private static readonly Dictionary<Block, string> SectionByBlock = new()
    {
        [Block.Detail] = "message",
        [Block.Footer] = "footer",
        [Block.Header] = "header",
    };

    // Lines and blocks are aligned one-to-one, in Ligne order, as produced by BlockAssigner. The
    // Descripteur has already been validated, so Position and Size are non-negative integers and every
    // Champ has an Id that is a legal XML element name.
    public static NormalizedXmlResult Build(
        IReadOnlyList<string> lines,
        IReadOnlyList<Block> blocks,
        XElement descriptorRoot)
    {
        List<ConversionError> errors = [];
        XElement file = new("file");

        for (int i = 0; i < lines.Count; i++)
        {
            if (!SectionByBlock.TryGetValue(blocks[i], out string? sectionName))
            {
                continue;
            }

            XElement? section = descriptorRoot.Element(sectionName);
            if (section is null)
            {
                continue;
            }

            XElement blocElement = new(sectionName);

            // Champs are emitted in Descripteur declaration order, which is the stable order the XSD of
            // each format relies on (R-4).
            foreach (XElement champ in section.Elements("value"))
            {
                string id = (string)champ.Attribute("Id")!;
                string rawValue = ExtractRawValue(lines[i], champ);
                string? datatype = (string?)champ.Attribute("datatype");

                ConversionError? error = Normalize(datatype, rawValue, blocks[i], i + 1, id, out string canonicalValue);
                if (error is not null)
                {
                    // Every typing Error is collected: the whole Fichier is scanned so the caller sees
                    // all of them at once rather than fixing and rerunning one at a time.
                    errors.Add(error);
                    continue;
                }

                blocElement.Add(new XElement(id, canonicalValue));
            }

            file.Add(blocElement);
        }

        if (errors.Count > 0)
        {
            return new NormalizedXmlResult { Errors = errors };
        }

        return new NormalizedXmlResult { Xml = Serialize(file) };
    }

    private static string ExtractRawValue(string line, XElement champ)
    {
        int position = int.Parse((string)champ.Attribute("Position")!, NumberStyles.None, CultureInfo.InvariantCulture);
        int size = int.Parse((string)champ.Attribute("Size")!, NumberStyles.None, CultureInfo.InvariantCulture);

        // A Champ can start past the end of the Ligne (a truncated or entirely absent trailing Filler,
        // tolerated by Story 1.5); its raw value is then empty.
        if (position >= line.Length)
        {
            return string.Empty;
        }

        // A Champ can start inside the Ligne but declare a Size that overruns its end (the last Champ
        // of a Bloc, or any Champ on a Ligne whose tail bytes are missing); the slice is clamped to
        // what the Ligne actually carries.
        int available = Math.Min(size, line.Length - position);
        return line.Substring(position, available);
    }

    private static ConversionError? Normalize(
        string? datatype,
        string rawValue,
        Block bloc,
        int lineNumber,
        string fieldId,
        out string canonicalValue)
    {
        // The int datatype is the only one that both constrains and rewrites the value in Step 1;
        // decimal, datetime and convert are added in Story 1.8. Every other Champ is a string.
        if (datatype == "int")
        {
            return NormalizeInteger(rawValue, bloc, lineNumber, fieldId, out canonicalValue);
        }

        // A string Champ keeps its internal spaces and only the fixed-width space padding is trimmed
        // from the end (AC-FR5-3, AC-FR5-7).
        canonicalValue = rawValue.TrimEnd(' ');
        return null;
    }

    private static ConversionError? NormalizeInteger(
        string rawValue,
        Block bloc,
        int lineNumber,
        string fieldId,
        out string canonicalValue)
    {
        string trimmed = rawValue.Trim(' ');

        // A blank int Champ yields an empty element; the NOT NULL obligation is judged later in Step 2
        // against the target column (AC-FR5-4, AC-FR5-6).
        if (trimmed.Length == 0)
        {
            canonicalValue = string.Empty;
            return null;
        }

        // Int Champs are always unsigned and must fit a 32-bit integer (D17); NumberStyles.None rejects
        // a sign, surrounding whitespace, a decimal point and group separators.
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
        {
            canonicalValue = string.Empty;
            return new ConversionError
            {
                Block = bloc,
                Code = ErrorCode.InvalidInteger,
                FieldId = fieldId,
                LineNumber = lineNumber,
                Message =
                    $"La valeur « {trimmed} » du Champ « {fieldId} » n'est pas un entier non signé valide.",
                RawValue = rawValue,
            };
        }

        // Parsing then formatting yields the canonical form, dropping leading zeros; an all-zero raw
        // value normalizes to "0" (AC-FR5-4).
        canonicalValue = parsed.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    private static string Serialize(XElement file)
    {
        XmlWriterSettings settings = new()
        {
            // No BOM, no indentation: byte-for-byte determinism between calls (AC-FR5-10, AC-FR5-11).
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using Utf8StringWriter writer = new();
        using (XmlWriter xmlWriter = XmlWriter.Create(writer, settings))
        {
            file.Save(xmlWriter);
        }

        return writer.ToString();
    }

    // StringWriter reports UTF-16 by default, which would land in the XML declaration; this override
    // makes XmlWriter emit encoding="utf-8" (AC-FR5-10).
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}

// Outcome of NormalizedXmlBuilder.Build. On success Errors is empty and Xml holds the normalized XML;
// on failure Errors carries every typing Error found and Xml is null.
// Properties are declared in alphabetical order (CC-4).
internal sealed record NormalizedXmlResult
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public string? Xml { get; init; }
}
