using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.Serialization;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 2.5 (AC-FR8-5, AC-FR8-6): the per-file half of FR-8. Once the worker is running, a value that
// Step 1 left null or blank for a NOT NULL column is a rejected Fichier, not a startup fault. Check
// walks the deserialized Detail block and returns one RequiredFieldMissing per empty NOT NULL column,
// ordered by the Descripteur Champ order. Empty means null for a typed Champ, null or whitespace for a
// string Champ.
public static class RequiredFieldCheck
{
    // A P60 Fichier is Header + Detail + Footer, so the Detail Bloc is always the second Ligne (D3).
    private const int DetailLineNumber = 2;

    // Reflection over Kape22FileMessage is cheap but constant, so resolve the ordered required-column
    // list once. Each entry pairs the Descripteur Champ Id (the DTO XML element name) with the DTO
    // property and the NOT NULL L_D_KAPE22 column it feeds. The list is ordered by the Descripteur
    // <value> Position so Check reports one error per column in Descripteur order (AC-FR8-6).
    private static readonly IReadOnlyList<RequiredChamp> RequiredChamps = BuildRequiredChamps();

    public static IReadOnlyList<ConversionError> Check(Kape22File file)
    {
        Kape22FileMessage message = file.Message;

        List<ConversionError> errors = [];
        foreach (RequiredChamp champ in RequiredChamps)
        {
            if (IsBlank(champ.Property.GetValue(message)))
            {
                errors.Add(new ConversionError
                {
                    Block = Block.Detail,
                    Code = ErrorCode.RequiredFieldMissing,
                    Column = champ.Column,
                    FieldId = champ.ChampId,
                    LineNumber = DetailLineNumber,
                    Message = $"Le Champ obligatoire '{champ.ChampId}' est vide (colonne {champ.Column} NOT NULL).",
                });
            }
        }

        return errors;
    }

    private static bool IsBlank(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        _ => false,
    };

    private static IReadOnlyList<RequiredChamp> BuildRequiredChamps()
    {
        NullabilityInfoContext nullability = new();
        IReadOnlyDictionary<string, int> positionByChampId = DescriptorPositions();

        return typeof(Kape22FileMessage).GetProperties()
            .Where(property => !Kape22Mapper.IsIgnored(property.Name))
            .Select(property => new
            {
                Property = property,
                Column = typeof(L_D_KAPE22).GetProperty(
                    Kape22Mapper.ResolveTargetName(property.Name),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase),
            })
            .Where(pair => pair.Column is not null
                && IsRequired(pair.Column!, nullability)
                && !StartupCompatibilityCheck.DerivedRequiredColumns.Contains(pair.Column!.Name))
            .Select(pair => new RequiredChamp(ChampId(pair.Property), pair.Column!.Name, pair.Property))
            // Order by the Descripteur <value> Position, not the DTO reflection order (AC-FR8-6).
            .OrderBy(champ => positionByChampId.TryGetValue(champ.ChampId, out int position) ? position : int.MaxValue)
            .ToList();
    }

    // The Descripteur Champ Id for a DTO property: its explicit [XmlElement] name when present, else
    // the property name.
    private static string ChampId(PropertyInfo property)
    {
        string? elementName = property.GetCustomAttribute<XmlElementAttribute>()?.ElementName;
        return string.IsNullOrEmpty(elementName) ? property.Name : elementName;
    }

    // Maps each Detail Champ Id to its Position in the embedded Descripteur, the authoritative source
    // for AC-FR8-6's "ordered by the Descripteur Champ order".
    private static IReadOnlyDictionary<string, int> DescriptorPositions()
    {
        XElement message = XDocument.Parse(EmbeddedDescriptor.Xml).Root?.Element("message")
            ?? throw new InvalidOperationException("The embedded P60 Descripteur has no <message> section.");

        return message.Elements("value")
            .Where(value => value.Attribute("Id") is not null && value.Attribute("Position") is not null)
            .ToDictionary(
                value => (string)value.Attribute("Id")!,
                value => int.Parse((string)value.Attribute("Position")!, CultureInfo.InvariantCulture));
    }

    // A column is required when it is a non-nullable value type or a non-nullable reference type.
    private static bool IsRequired(PropertyInfo column, NullabilityInfoContext nullability)
    {
        Type type = column.PropertyType;
        if (type.IsValueType)
        {
            return Nullable.GetUnderlyingType(type) is null;
        }

        return nullability.Create(column).WriteState == NullabilityState.NotNull;
    }

    // Properties are declared in alphabetical order (CC-4).
    private sealed record RequiredChamp(string ChampId, string Column, PropertyInfo Property);
}
