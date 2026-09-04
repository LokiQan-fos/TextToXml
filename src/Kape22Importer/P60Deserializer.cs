using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using TextToXml;

namespace Kape22Importer;

// Step 2 entry gate (AC-FR7-1, AC-FR5-14, AC-FR5-12b): validate the normalized XML against the
// embedded P60.xsd, then deserialize it into Kape22File. A schema violation is a safety net that
// should never fire once Step 1 has succeeded, so it comes back as a single File-level
// PersistenceError rather than an exception. Story 2.4's Kape22Mapper builds its mapping on top of this.
public static class P60Deserializer
{
    // The embedded P60.xsd, pinned by Kape22Importer.csproj.
    private const string SchemaResourceName = "Kape22Importer.Templates.P60.xsd";

    // Built once: compiling the schema set and constructing the XmlSerializer are both costly.
    private static readonly Lazy<XmlSchemaSet> Schema = new(LoadSchema);

    private static readonly Lazy<XmlSerializer> Serializer = new(() => new XmlSerializer(typeof(Kape22File)));

    public static P60DeserializeResult Deserialize(string normalizedXml)
    {
        ConversionError? schemaError = Validate(normalizedXml);
        if (schemaError is not null)
        {
            return new P60DeserializeResult { Errors = [schemaError] };
        }

        using StringReader reader = new(normalizedXml);
        Kape22File file = (Kape22File)Serializer.Value.Deserialize(reader)!;
        return new P60DeserializeResult { File = file };
    }

    // Validates the document against P60.xsd and returns the first violation as a File-level
    // PersistenceError, or null when it conforms. Never throws: a document that is not even well formed
    // is reported as a violation too.
    private static ConversionError? Validate(string normalizedXml)
    {
        ConversionError? failure = null;

        XmlReaderSettings settings = new()
        {
            Schemas = Schema.Value,
            ValidationType = ValidationType.Schema,
        };
        settings.ValidationEventHandler += (_, args) =>
            failure ??= new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.PersistenceError,
                Message = $"Le XML normalisé n'est pas conforme à P60.xsd : {args.Message}",
            };

        try
        {
            using XmlReader reader = XmlReader.Create(new StringReader(normalizedXml), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            failure ??= new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.PersistenceError,
                Message = $"Le XML normalisé n'est pas un document XML bien formé : {exception.Message}",
            };
        }

        return failure;
    }

    private static XmlSchemaSet LoadSchema()
    {
        Assembly importer = typeof(P60Deserializer).Assembly;
        using Stream stream = importer.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{SchemaResourceName}' is missing from {importer.GetName().Name}.");

        XmlSchemaSet schemas = new();
        using XmlReader reader = XmlReader.Create(stream);
        schemas.Add(null, reader);
        schemas.Compile();
        return schemas;
    }
}

// Outcome of P60Deserializer.Deserialize. On success Errors is empty and File carries the DTO; on
// schema non-conformance Errors holds one File-level PersistenceError and File is null.
// Properties are declared in alphabetical order (CC-4).
public sealed record P60DeserializeResult
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public Kape22File? File { get; init; }
}
