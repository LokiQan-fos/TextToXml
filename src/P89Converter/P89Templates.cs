using System;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace P89Converter;

// The P89 Descripteur and schema embedded in this assembly, read once. Templates/P89.xsd is generated
// from Templates/P89.xml by scripts/gen.ps1 -Format P89.
public static class P89Templates
{
    private const string SchemaResourceName = "P89Converter.Templates.P89.xsd";

    public static string DescriptorXml { get; } = Read("P89Converter.Templates.P89.xml");

    // The embedded schema compiled once, not once per Fichier. Static initializers run in textual order,
    // so Compile reads the resource itself instead of depending on SchemaXsd, declared after it (CC-4).
    internal static XmlSchemaSet Schemas { get; } = Compile();

    public static string SchemaXsd { get; } = Read(SchemaResourceName);

    private static XmlSchemaSet Compile()
    {
        XmlSchemaSet schemas = new();
        using XmlReader reader = XmlReader.Create(new StringReader(Read(SchemaResourceName)));
        schemas.Add(null, reader);
        schemas.Compile();
        return schemas;
    }

    private static string Read(string resourceName)
    {
        Assembly assembly = typeof(P89Templates).Assembly;

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{resourceName}' is missing from {assembly.GetName().Name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
