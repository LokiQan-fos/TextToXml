using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace TextToXml;

// Reads the Descripteur directly, without a meta-schema (D10), and returns the first layout problem
// found, or null when the Descripteur is well-formed and valid. Every problem is reported the same
// way: a single LayoutInvalid error carried by the whole Fichier (FR-1).
internal static class DescriptorValidator
{
    private static readonly HashSet<string> KnownDatatypes =
        new(StringComparer.Ordinal) { "string", "int", "decimal", "datetime" };

    // The three Bloc sections, in the order the skeleton [header] + message(s) + [footer] imposes.
    private static readonly string[] SectionNames = ["header", "message", "footer"];

    public static ConversionError? Validate(string descriptor)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(descriptor);
        }
        catch (XmlException exception)
        {
            return LayoutInvalid($"Le descripteur XML n'est pas bien formé : {exception.Message}");
        }

        XElement root = document.Root!;

        // The Descriptor root is <commande> in no namespace; the element names below are matched verbatim.
        if (root.Name != "commande")
        {
            return LayoutInvalid(
                $"La racine du descripteur doit être <commande> sans espace de noms, trouvé <{root.Name.LocalName}>.");
        }

        // Only format="Fixed" is implemented in v1; "Semicolon" carries its own message (D24).
        string format = (string?)root.Attribute("format") ?? string.Empty;
        if (format != "Fixed")
        {
            string reason = format == "Semicolon"
                ? "non supporté en v1 ; seul « Fixed » est implémenté"
                : "non reconnu ; seul « Fixed » est implémenté";
            return LayoutInvalid($"Format « {format} » {reason}.");
        }

        if (root.Element("message") is null)
        {
            return LayoutInvalid("Le descripteur ne contient pas de section <message> obligatoire.");
        }

        foreach (string sectionName in SectionNames)
        {
            XElement? section = root.Element(sectionName);
            if (section is null)
            {
                continue;
            }

            ConversionError? sectionError = ValidateSection(section, sectionName);
            if (sectionError is not null)
            {
                return sectionError;
            }
        }

        // The Segment control is optional; when a segmentField is named it must exist in every Bloc present.
        string? segmentField = (string?)root.Attribute("segmentField");
        if (!string.IsNullOrEmpty(segmentField))
        {
            foreach (string sectionName in SectionNames)
            {
                XElement? section = root.Element(sectionName);
                if (section is null)
                {
                    continue;
                }

                bool present = section.Elements("value")
                    .Any(value => (string?)value.Attribute("Id") == segmentField);
                if (!present)
                {
                    return LayoutInvalid($"segmentField « {segmentField} » est absent du Bloc {sectionName}.");
                }
            }
        }

        return null;
    }

    private static ConversionError? ValidateSection(XElement section, string sectionName)
    {
        HashSet<string> seenIds = new(StringComparer.Ordinal);

        foreach (XElement value in section.Elements("value"))
        {
            string? id = (string?)value.Attribute("Id");

            // A Champ with no Id cannot be named in the normalized XML.
            if (string.IsNullOrEmpty(id))
            {
                string position = (string?)value.Attribute("Position") ?? "?";
                return LayoutInvalid($"Un Champ du Bloc {sectionName} à la Position {position} n'a pas d'attribut Id.");
            }

            // Two Champs sharing an Id inside the same Bloc make the layout ambiguous.
            if (!seenIds.Add(id))
            {
                return LayoutInvalid($"Le Champ « {id} » est déclaré deux fois dans le Bloc {sectionName}.");
            }

            // Overlapping slices are allowed (D23); a missing, negative or non-integer offset is not.
            if (!IsNonNegativeInteger(value.Attribute("Position")) || !IsNonNegativeInteger(value.Attribute("Size")))
            {
                return LayoutInvalid(
                    $"Le Champ « {id} » a un attribut Position ou Size absent, négatif ou non entier.");
            }

            // The datatype attribute is optional and defaults to string; when present it must be a known type.
            string? datatype = (string?)value.Attribute("datatype");
            if (datatype is not null && !KnownDatatypes.Contains(datatype))
            {
                return LayoutInvalid($"Le Champ « {id} » a un datatype non reconnu : « {datatype} ».");
            }
        }

        return null;
    }

    private static bool IsNonNegativeInteger(XAttribute? attribute)
    {
        return attribute is not null
            && int.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= 0;
    }

    private static ConversionError LayoutInvalid(string message)
    {
        return new ConversionError
        {
            Block = Block.File,
            Code = ErrorCode.LayoutInvalid,
            LineNumber = 0,
            Message = message,
        };
    }
}
