using System;
using System.Collections.Generic;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 2.4: turns the Kape22File DTO's Detail block (Kape22FileMessage) into an insertable
// L_D_KAPE22 entity (PRD Annexe B). Default rule: copy by case-insensitive property name (types are
// already aligned, checked at worker startup by FR-8). NamingExceptions and IgnoredProperties are the
// only two deviations from that default, built once like P60Deserializer's Lazy<> setup.
// Header/Footer-derived columns (NumeroFichier, DateReception) are out of scope here (Story 2.6/2.7).
public static class Kape22Mapper
{
    // Annexe B's one naming exception: DTO property name -> L_D_KAPE22 property name.
    public static readonly IReadOnlyDictionary<string, string> NamingExceptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OFOriginInterne"] = "OForiginInterne",
        };

    // Annexe B's ignored DTO properties: envelope/reserved fields and the Four1/Four2 date+heure
    // pairs (derived elsewhere, §0bis D14), which have no direct L_D_KAPE22 column.
    public static readonly IReadOnlySet<string> IgnoredProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Kape22FileMessage.File),
        nameof(Kape22FileMessage.Date),
        nameof(Kape22FileMessage.NumeroFichier),
        nameof(Kape22FileMessage.Segment),
        nameof(Kape22FileMessage.Element),
        nameof(Kape22FileMessage.KAP),
        nameof(Kape22FileMessage.DateEnfournementFour1_Date),
        nameof(Kape22FileMessage.DateEnfournementFour1_Heure),
        nameof(Kape22FileMessage.DateEnfournementFour2_Date),
        nameof(Kape22FileMessage.DateEnfournementFour2_Heure),
    };

    // Any Champ Id starting with "Reserve" is ignored too (ReserveRefroidissoir, ReserveRefroidissoir2/3).
    public static bool IsIgnored(string dtoPropertyName) =>
        IgnoredProperties.Contains(dtoPropertyName)
        || dtoPropertyName.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase);

    // Applies Annexe B's naming exception, if any, else falls back to the DTO property name.
    public static string ResolveTargetName(string dtoPropertyName) =>
        NamingExceptions.TryGetValue(dtoPropertyName, out string? renamed) ? renamed : dtoPropertyName;

    // sourceFileName is accepted per the PRD API signature but unused until Story 2.7's file-name
    // coherence check.
    public static MapResult<L_D_KAPE22> Map(string normalizedXml, string sourceFileName)
    {
        P60DeserializeResult deserialized = P60Deserializer.Deserialize(normalizedXml);
        if (deserialized.File is null)
        {
            return new MapResult<L_D_KAPE22> { Errors = deserialized.Errors };
        }

        L_D_KAPE22 entity = new();
        Kape22FileMessage message = deserialized.File.Message;

        foreach (PropertyInfo source in typeof(Kape22FileMessage).GetProperties())
        {
            if (IsIgnored(source.Name))
            {
                continue;
            }

            string targetName = ResolveTargetName(source.Name);
            PropertyInfo target = typeof(L_D_KAPE22).GetProperty(
                targetName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

            object? value = source.GetValue(message) ?? DefaultForNonNullable(target.PropertyType);
            target.SetValue(entity, value);
        }

        return new MapResult<L_D_KAPE22> { Value = entity };
    }

    // A blank DTO value (e.g. Indice) must not throw when the target column is a non-nullable value
    // type (int, not int?): substitute its default (0) instead. Nullable<T> targets stay null.
    public static object? DefaultForNonNullable(Type targetType) =>
        targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
            ? Activator.CreateInstance(targetType)
            : null;
}

// Outcome of Kape22Mapper.Map. Mirrors ConversionResult/P60DeserializeResult: Success is true exactly
// when Errors is empty; Value is null on failure.
// Properties are declared in alphabetical order (CC-4).
public sealed record MapResult<T>
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public bool Success => Errors.Count == 0;

    public T? Value { get; init; }
}
