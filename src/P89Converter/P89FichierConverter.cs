using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using TextToXml;

namespace P89Converter;

// The Step 1 pipeline for one P89 Fichier (Story 5.1). The P89 Fichiers are UTF-8 while TextToXml reads
// Windows-1252 bytes (D29), so the content is transcoded first. Both halves are strict: a malformed
// UTF-8 byte or a character outside Windows-1252 fails the Fichier instead of being replaced, which
// would shift the fixed-width Positions. The normalized XML is then validated against P89.xsd.
public static class P89FichierConverter
{
    private static readonly int NumeroFichierSize = ChampSize("header", "NumeroFichier");

    private static readonly int OfSize = ChampSize("message", "OF");

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding Windows1252 = CreateWindows1252();

    public static P89Conversion Convert(byte[] content) => Convert(content, P89Templates.Schemas);

    // The schema is a seam so a test can stand in for a schema error no raw Fichier can produce.
    internal static P89Conversion Convert(byte[] content, XmlSchemaSet schemas)
    {
        byte[] input;
        try
        {
            input = Windows1252.GetBytes(Utf8.GetString(content));
        }
        catch (Exception exception) when (exception is DecoderFallbackException or EncoderFallbackException)
        {
            return new P89Conversion { Reasons = [$"Encodage : {exception.Message}"] };
        }

        ConversionResult result = Converter.Convert(input, P89Templates.DescriptorXml);
        if (!result.Success)
        {
            return new P89Conversion
            {
                Reasons = [.. result.Errors.Select(error => $"{error.Code} ligne {error.LineNumber} {error.FieldId} : {error.Message}")],
            };
        }

        List<string> reasons = [];
        XDocument xml = XDocument.Parse(result.Xml!);
        xml.Validate(schemas, (_, args) => reasons.Add($"XSD : {args.Message}"));

        // The OF and NumeroFichier are readable as soon as a normalized XML exists, even a schema-invalid
        // one, so the IFichierJournal entry of a rejected Fichier still carries them.
        return new P89Conversion
        {
            NumeroFichier = Raw(xml.Root?.Element("header")?.Element("NumeroFichier")?.Value, NumeroFichierSize),
            OF = Raw(xml.Root?.Element("message")?.Element("OF")?.Value, OfSize),
            Reasons = reasons,
            Xml = result.Xml,
        };
    }

    // The Size of a Champ in the embedded Descripteur.
    private static int ChampSize(string bloc, string champId) =>
        (int)XDocument.Parse(P89Templates.DescriptorXml).Root!.Element(bloc)!.Elements("value")
            .First(value => (string?)value.Attribute("Id") == champId)
            .Attribute("Size")!;

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    // The normalized XML drops the leading zeros of an int Champ; the IFichierJournal entry carries the raw,
    // zero-padded Fichier value instead, e.g. NumeroFichier "013", not "13".
    private static string? Raw(string? value, int size) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().PadLeft(size, '0');
}

// The outcome of P89FichierConverter.Convert. Xml is set when the Converter succeeded, even if the schema
// then rejected it; Success means no reason at all. Properties are declared in alphabetical order (CC-4).
public sealed class P89Conversion
{
    public string? NumeroFichier { get; init; }

    public string? OF { get; init; }

    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool Success => this.Reasons.Count == 0;

    public string? Xml { get; init; }
}
