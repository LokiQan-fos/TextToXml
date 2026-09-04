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
            string? sectionName = DescriptorSections.For(blocks[i]);
            if (sectionName is null)
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

                ConversionError? error = Normalize(champ, rawValue, blocks[i], i + 1, id, out string? canonicalValue);
                if (error is not null)
                {
                    // Every typing Error is collected: the whole Fichier is scanned so the caller sees
                    // all of them at once rather than fixing and rerunning one at a time.
                    errors.Add(error);
                    continue;
                }

                // A typed Champ (int, decimal, datetime) with a blank Valeur brute normalizes to null:
                // its element is omitted, since an empty value is not a valid xs:int / xs:decimal /
                // xs:dateTime and P60.xsd types those elements minOccurs="0" (PRD D27). A string Champ
                // never normalizes to null, so its empty element is still emitted (AC-FR5-6).
                if (canonicalValue is null)
                {
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

    // A null canonicalValue means "omit this element": a typed Champ whose Valeur brute is blank. An
    // empty (but non-null) canonicalValue is a genuine empty string element.
    private static ConversionError? Normalize(
        XElement champ,
        string rawValue,
        Block bloc,
        int lineNumber,
        string fieldId,
        out string? canonicalValue)
    {
        // The datatype dictates the canonical form written to the normalized XML. The int, decimal and
        // datetime datatypes all constrain and rewrite the Valeur brute in Step 1; every other Champ is
        // a string.
        string? datatype = (string?)champ.Attribute("datatype");

        if (datatype == "int")
        {
            return NormalizeInteger(rawValue, bloc, lineNumber, fieldId, out canonicalValue);
        }

        if (datatype == "decimal")
        {
            return NormalizeDecimal(champ, rawValue, bloc, lineNumber, fieldId, out canonicalValue);
        }

        if (datatype == "datetime")
        {
            return NormalizeDateTime(champ, rawValue, bloc, lineNumber, fieldId, out canonicalValue);
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
        out string? canonicalValue)
    {
        string trimmed = rawValue.Trim(' ');

        // A blank int Champ omits its element (an empty value is not a valid xs:int); the NOT NULL
        // obligation is judged later in Step 2 against the target column (AC-FR5-4, PRD D27).
        if (trimmed.Length == 0)
        {
            canonicalValue = null;
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

    private static ConversionError? NormalizeDecimal(
        XElement champ,
        string rawValue,
        Block bloc,
        int lineNumber,
        string fieldId,
        out string? canonicalValue)
    {
        string trimmed = rawValue.Trim(' ');

        // A blank decimal Champ omits its element, like a blank int; the obligation is judged in
        // Step 2 against the target column (PRD D27).
        if (trimmed.Length == 0)
        {
            canonicalValue = null;
            return null;
        }

        // The Descripteur names the decimal separator used in the Valeur brute; the descriptor validator
        // has checked it is a single character. It defaults to the point and is the only non-digit the
        // value may carry besides a leading sign (CTR-1). The convert attribute is not used for decimal
        // in v1.
        string? separator = (string?)champ.Attribute("decimalSeparator");
        char effectiveSeparator = string.IsNullOrEmpty(separator) ? '.' : separator[0];

        // A leading sign is allowed for decimal (unlike int); a group separator, an inner space, the
        // wrong separator character or any other stray character makes the value invalid rather than
        // letting it parse to a plausible wrong number.
        if (!IsPlainDecimal(trimmed, effectiveSeparator))
        {
            canonicalValue = string.Empty;
            return InvalidDecimal(bloc, lineNumber, fieldId, trimmed, rawValue);
        }

        string invariant = effectiveSeparator == '.' ? trimmed : trimmed.Replace(effectiveSeparator, '.');
        if (!decimal.TryParse(
            invariant,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out decimal parsed))
        {
            canonicalValue = string.Empty;
            return InvalidDecimal(bloc, lineNumber, fieldId, trimmed, rawValue);
        }

        // The canonical form drops trailing zeros the way the int form drops leading zeros, so "12,50"
        // and "12,5" yield the same normalized value (CTR-1). The run of '#' covers the decimal's full
        // precision and the pattern never falls back to exponential notation.
        canonicalValue = parsed.ToString("0.#############################", CultureInfo.InvariantCulture);
        return null;
    }

    // Accepts an optional leading sign, decimal digits and at most one occurrence of the declared
    // separator; at least one digit must be present.
    private static bool IsPlainDecimal(string value, char separator)
    {
        int start = value.Length > 0 && value[0] is '+' or '-' ? 1 : 0;
        bool separatorSeen = false;
        bool digitSeen = false;

        for (int i = start; i < value.Length; i++)
        {
            char c = value[i];
            if (c == separator)
            {
                if (separatorSeen)
                {
                    return false;
                }

                separatorSeen = true;
            }
            else if (char.IsAsciiDigit(c))
            {
                digitSeen = true;
            }
            else
            {
                return false;
            }
        }

        return digitSeen;
    }

    private static ConversionError InvalidDecimal(Block bloc, int lineNumber, string fieldId, string trimmed, string rawValue)
    {
        return new ConversionError
        {
            Block = bloc,
            Code = ErrorCode.InvalidDecimal,
            FieldId = fieldId,
            LineNumber = lineNumber,
            Message = $"La valeur « {trimmed} » du Champ « {fieldId} » n'est pas un nombre décimal valide.",
            RawValue = rawValue,
        };
    }

    private static ConversionError? NormalizeDateTime(
        XElement champ,
        string rawValue,
        Block bloc,
        int lineNumber,
        string fieldId,
        out string? canonicalValue)
    {
        string trimmed = rawValue.Trim(' ');

        // A blank datetime Champ omits its element, like a blank int or decimal (PRD D27).
        if (trimmed.Length == 0)
        {
            canonicalValue = null;
            return null;
        }

        // The convert attribute is a "{0:<mask>}" composite format string whose mask lays out the Valeur
        // brute; the descriptor validator has already guaranteed a datetime Champ carries a usable one.
        // The mask drives parsing only - the output is always ISO-8601 (CTR-2). ParseExact with
        // InvariantCulture resolves a two-digit year ("yy") through the Gregorian TwoDigitYearMax pivot
        // (currently 2049), so a format needing an unambiguous year must use a four-digit "yyyy" mask.
        string mask = DescriptorValidator.ExtractConvertMask((string?)champ.Attribute("convert"))!;

        if (!DateTime.TryParseExact(trimmed, mask, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
        {
            canonicalValue = string.Empty;
            return new ConversionError
            {
                Block = bloc,
                Code = ErrorCode.InvalidDate,
                FieldId = fieldId,
                LineNumber = lineNumber,
                Message = $"La valeur « {trimmed} » du Champ « {fieldId} » n'est pas une date valide.",
                RawValue = rawValue,
            };
        }

        // The normalized XML carries an ISO-8601 value: a date alone when the convert mask has no time
        // token, a date and time otherwise, so an XmlSerializer reads it straight into a DateTime
        // without a custom converter (CTR-2, CTR-3).
        canonicalValue = MaskHasTimeComponent(mask)
            ? value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return null;
    }

    private static bool MaskHasTimeComponent(string mask)
    {
        // In a .NET custom date/time format string the time-bearing specifiers are h, H, m, s, f, F, t,
        // z and K; d, M, y and g are date-only. A lowercase m is minutes, an uppercase M is the month.
        // A character inside a quoted literal ('...' or "...") and an escaped character (\c) are skipped
        // so a literal letter is never read as a specifier.
        for (int i = 0; i < mask.Length; i++)
        {
            char specifier = mask[i];
            if (specifier == '\\')
            {
                i++;
                continue;
            }

            if (specifier is '\'' or '"')
            {
                i = mask.IndexOf(specifier, i + 1);
                if (i < 0)
                {
                    return false;
                }

                continue;
            }

            if (specifier is 'h' or 'H' or 'm' or 's' or 'f' or 'F' or 't' or 'z' or 'K')
            {
                return true;
            }
        }

        return false;
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
