using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Serialization;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.3: a hand-written P60.xsd describes the normalized <file> XML, the Kape22File DTO is
// generated from it and committed, and the normalized XML is validated against the schema before
// deserialization (AR-3, AR-4, D10). These tests are written test-first (CC-1): they read the embedded
// descriptor, the embedded schema and the reference Fichiers only, so they need no database and stay
// in the Unit category. Every behavioural assertion fails red until Templates/P60.xsd, the generated
// Kape22File and P60Deserializer.Deserialize ship, then turns green. Each AC-named test carries its
// [Trait("AC", "FRx-y")] so the trait-filtered coverage view sees it (CC-5).
[Trait("Category", TestCategory.Unit)]
public class P60XsdTests
{
    // The LogicalName Kape22Importer.csproj will pin for the embedded schema, mirroring the P60.xml
    // convention from Story 2.2.
    private const string EmbeddedP60XsdResourceName = "Kape22Importer.Templates.P60.xsd";

    private const string EmbeddedP60XmlResourceName = "Kape22Importer.Templates.P60.xml";

    private static readonly XNamespace Xs = "http://www.w3.org/2001/XMLSchema";

    // The three normalized-XML sections, in skeleton order.
    public static TheoryData<string> BlocNames() => new() { "header", "message", "footer" };

    // The ten Annexe A.4 reference Fichiers, the same set the Story 2.2 tests reuse.
    public static TheoryData<string> ReferenceFichierNames()
    {
        TheoryData<string> data = new();
        for (int number = 1; number <= 10; number++)
        {
            data.Add($"P60_847_682_{number:D3}");
        }

        return data;
    }

    [Fact]
    public void P60Xsd_IsEmbeddedInTheImporterAssembly()
    {
        Assembly importer = typeof(P60Deserializer).Assembly;

        Assert.Contains(EmbeddedP60XsdResourceName, importer.GetManifestResourceNames());

        XElement root = XDocument.Parse(EmbeddedP60Xsd()).Root!;
        Assert.Equal("schema", root.Name.LocalName);
        Assert.Equal(Xs.NamespaceName, root.Name.NamespaceName);
    }

    // AC-FR1-13: every <value Id> of the descriptor has an <xs:element name="Id"> of the matching type
    // in the P60.xsd complexType for its Bloc - xs:int for datatype="int", xs:string for everything else.
    // No descriptor <value> is schema-ignored: Story 1.6 emits every one of them into the normalized
    // XML (a string Champ stays present even when blank, AC-FR5-6), so P60.xsd must declare all of them.
    [Theory]
    [MemberData(nameof(BlocNames))]
    [Trait("AC", "FR1-13")]
    public void P60Xsd_EveryDescriptorValueHasAMatchingTypedElement_AcFr1_13(string bloc)
    {
        XElement descriptorBloc = DescriptorRoot().Element(bloc)!;
        Dictionary<string, string> schemaTypes =
            XsdBlocElements(bloc).ToDictionary(element => element.Name, element => element.Type);

        List<string> problems = [];
        foreach (XElement value in descriptorBloc.Elements("value"))
        {
            string id = (string)value.Attribute("Id")!;
            string expected = (string?)value.Attribute("datatype") == "int" ? "int" : "string";

            if (!schemaTypes.TryGetValue(id, out string? actual))
            {
                problems.Add($"{bloc}/{id}: no matching <xs:element> in P60.xsd.");
            }
            else if (actual != expected)
            {
                problems.Add($"{bloc}/{id}: P60.xsd type is xs:{actual}, descriptor datatype expects xs:{expected}.");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // AC-FR1-13: header/footer presence in P60.xsd matches the descriptor - P60 has both, so <file>
    // declares header, message and footer.
    [Fact]
    [Trait("AC", "FR1-13")]
    public void P60Xsd_FileSectionsMatchTheDescriptor_AcFr1_13()
    {
        string[] sections = FileSectionElements()
            .Select(element => (string)element.Attribute("name")!)
            .ToArray();

        XElement descriptor = DescriptorRoot();
        Assert.Equal(descriptor.Element("header") is not null, sections.Contains("header"));
        Assert.Equal(descriptor.Element("footer") is not null, sections.Contains("footer"));
        Assert.Contains("message", sections);
    }

    // AC-FR1-13 / PRD D27: every typed element (xs:int in v1) is minOccurs="0", since Step 1 omits a
    // blank typed Champ from the normalized XML and the DTO receives int?.
    [Theory]
    [MemberData(nameof(BlocNames))]
    [Trait("AC", "FR1-13")]
    public void P60Xsd_TypedElementsAreOptional_AcFr1_13(string bloc)
    {
        List<string> offenders = XsdBlocElements(bloc)
            .Where(element => element.Type == "int" && !element.Optional)
            .Select(element => $"{bloc}/{element.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"int elements must be minOccurs=\"0\" (PRD D27): {string.Join(", ", offenders)}.");
    }

    // R-4: each P60.xsd <xs:sequence> lists the Bloc's elements in the exact order of the descriptor
    // <value> declarations, so the normalized XML (emitted in descriptor order, Story 1.6) validates.
    [Theory]
    [MemberData(nameof(BlocNames))]
    public void P60Xsd_SequenceOrderMatchesDescriptorValueOrder(string bloc)
    {
        string[] descriptorOrder = DescriptorRoot().Element(bloc)!
            .Elements("value")
            .Select(value => (string)value.Attribute("Id")!)
            .ToArray();

        string[] schemaOrder = XsdBlocElements(bloc)
            .Select(element => element.Name)
            .ToArray();

        Assert.Equal(descriptorOrder, schemaOrder);
    }

    // AC-FR5-14: the normalized XML of every valid reference Fichier validates against P60.xsd
    // (XmlReader + schema), with no validation event raised.
    [Theory]
    [MemberData(nameof(ReferenceFichierNames))]
    [Trait("AC", "FR5-14")]
    public void Converter_ReferenceFichier_NormalizedXmlValidatesAgainstP60Xsd_AcFr5_14(string fichierName)
    {
        ConversionResult result = Converter.Convert(ReadValidFixture(fichierName), EmbeddedP60Xml());
        Assert.True(result.Success, $"{fichierName} did not convert cleanly.");

        IReadOnlyList<string> schemaErrors = SchemaValidationErrors(result.Xml!);

        Assert.True(schemaErrors.Count == 0, $"{fichierName}: {string.Join(Environment.NewLine, schemaErrors)}");
    }

    // AC-FR7-1: a normalized XML that does not conform to P60.xsd yields a single
    // {Block:File, Code:PersistenceError} citing the schema error, and no DTO.
    [Fact]
    [Trait("AC", "FR7-1")]
    public void P60Deserializer_NonConformantXml_YieldsFileLevelPersistenceError_AcFr7_1()
    {
        P60DeserializeResult result = P60Deserializer.Deserialize(NonConformantNormalizedXml());

        Assert.Null(result.File);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.PersistenceError, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // AC-FR7-1: the schema safety net never throws - a non-conforming document comes back as an Error.
    [Fact]
    [Trait("AC", "FR7-1")]
    public void P60Deserializer_NonConformantXml_DoesNotThrow_AcFr7_1()
    {
        Assert.Null(Record.Exception(() => P60Deserializer.Deserialize(NonConformantNormalizedXml())));
    }

    // AC-FR5-12b: a valid Fichier round-trips value -> normalized XML -> Kape22File without loss;
    // int and string values are preserved.
    [Fact]
    [Trait("AC", "FR5-12b")]
    public void P60Deserializer_ValidFichier_RoundTripsIntoKape22File_AcFr5_12b()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture("P60_847_682_001"), EmbeddedP60Xml());
        Assert.True(conversion.Success);

        P60DeserializeResult result = P60Deserializer.Deserialize(conversion.Xml!);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.File);

        XElement file = XDocument.Parse(conversion.Xml!).Root!;
        XElement header = file.Element("header")!;
        XElement message = file.Element("message")!;
        XElement footer = file.Element("footer")!;

        Assert.Equal((string)header.Element("File")!, result.File!.Header.File);
        Assert.Equal((string)header.Element("NumeroFichier")!, result.File.Header.NumeroFichier);
        Assert.Equal((string)message.Element("OF")!, result.File.Message.OF);
        Assert.Equal((string)message.Element("Client")!, result.File.Message.Client);
        Assert.Equal((string)footer.Element("Records")!, result.File.Footer.Records);

        // Indice is xs:int and minOccurs="0": its property mirrors the element, value-for-value or
        // null-for-absent.
        Assert.Equal(
            message.Element("Indice")?.Value,
            result.File.Message.Indice?.ToString(CultureInfo.InvariantCulture));
    }

    // AC-FR5-12b / PRD D27: a typed Champ omitted from the normalized XML deserializes to a null
    // property, not to zero. Indice is dropped here to stand in for a blank typed Champ.
    [Fact]
    [Trait("AC", "FR5-12b")]
    public void P60Deserializer_OmittedTypedChamp_DeserializesToNullProperty_AcFr5_12b()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture("P60_847_682_001"), EmbeddedP60Xml());
        Assert.True(conversion.Success);

        string withoutIndice = Regex.Replace(conversion.Xml!, "<Indice>[^<]*</Indice>", string.Empty);

        P60DeserializeResult result = P60Deserializer.Deserialize(withoutIndice);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.File);
        Assert.Null(result.File!.Message.Indice);
    }

    // AR-4: the generated DTO exposes Header, Message and Footer and binds to the <file> root.
    [Fact]
    public void Kape22File_ExposesHeaderMessageAndFooter()
    {
        Assert.NotNull(typeof(Kape22File).GetProperty("Header"));
        Assert.NotNull(typeof(Kape22File).GetProperty("Message"));
        Assert.NotNull(typeof(Kape22File).GetProperty("Footer"));
        Assert.Equal("file", typeof(Kape22File).GetCustomAttribute<XmlRootAttribute>()?.ElementName);
    }

    // AR-4 / R-5: Kape22File is hand-maintained, not tool-generated in CI, so a test keeps it in
    // lockstep with P60.xsd - same element names, same order, xs:int -> int? and xs:string -> string.
    [Theory]
    [InlineData("header", typeof(Kape22FileHeader))]
    [InlineData("message", typeof(Kape22FileMessage))]
    [InlineData("footer", typeof(Kape22FileFooter))]
    public void Kape22File_MirrorsP60Xsd(string bloc, Type dtoType)
    {
        List<(string Name, string Type, bool Optional)> schema = XsdBlocElements(bloc);

        (string Name, string Type)[] dto = dtoType.GetProperties()
            .Select(property => (
                Name: property.GetCustomAttribute<XmlElementAttribute>()?.ElementName ?? property.Name,
                Type: property.PropertyType == typeof(int?) ? "int" : "string"))
            .ToArray();

        Assert.Equal(
            schema.Select(element => (element.Name, element.Type)).ToArray(),
            dto);
    }

    // Builds a normalized XML that P60.xsd rejects by inserting an element the schema does not declare
    // as the first child of <message>.
    private static string NonConformantNormalizedXml()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture("P60_847_682_001"), EmbeddedP60Xml());
        return conversion.Xml!.Replace("<message>", "<message><Bogus>x</Bogus>", StringComparison.Ordinal);
    }

    // Validates an instance document against the embedded P60.xsd and returns every validation message.
    private static IReadOnlyList<string> SchemaValidationErrors(string xml)
    {
        List<string> errors = [];

        XmlSchemaSet schemas = new();
        using (XmlReader schemaReader = XmlReader.Create(new StringReader(EmbeddedP60Xsd())))
        {
            schemas.Add(null, schemaReader);
        }

        XmlReaderSettings settings = new()
        {
            Schemas = schemas,
            ValidationType = ValidationType.Schema,
        };
        settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);

        using XmlReader reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }

        return errors;
    }

    // The <xs:element> children of the <file> root's complexType, in document order.
    private static IEnumerable<XElement> FileSectionElements()
    {
        XElement schema = SchemaRoot();
        XElement file = GlobalElement(schema, "file");
        return SequenceElements(ComplexTypeOf(file, schema));
    }

    // The elements declared for a Bloc in P60.xsd: name, local type without the xs: prefix
    // (defaulting to string), and whether it is minOccurs="0".
    private static List<(string Name, string Type, bool Optional)> XsdBlocElements(string bloc)
    {
        XElement schema = SchemaRoot();
        XElement blocDecl = FileSectionElements()
            .First(element => string.Equals((string?)element.Attribute("name"), bloc, StringComparison.OrdinalIgnoreCase));

        return SequenceElements(ComplexTypeOf(blocDecl, schema))
            .Select(element => (
                Name: (string)element.Attribute("name")!,
                Type: StripPrefix((string?)element.Attribute("type") ?? "xs:string"),
                Optional: (string?)element.Attribute("minOccurs") == "0"))
            .ToList();
    }

    // Resolves an <xs:element>'s complexType, whether declared inline or referenced by a named type.
    private static XElement ComplexTypeOf(XElement elementDeclaration, XElement schema)
    {
        XElement? inline = elementDeclaration.Element(Xs + "complexType");
        if (inline is not null)
        {
            return inline;
        }

        string typeName = StripPrefix((string)elementDeclaration.Attribute("type")!);
        return schema.Elements(Xs + "complexType")
            .First(complexType => (string?)complexType.Attribute("name") == typeName);
    }

    private static IEnumerable<XElement> SequenceElements(XElement complexType) =>
        complexType.Element(Xs + "sequence")!.Elements(Xs + "element");

    private static XElement GlobalElement(XElement schema, string name) =>
        schema.Elements(Xs + "element").First(element => (string?)element.Attribute("name") == name);

    private static string StripPrefix(string qualifiedName) =>
        qualifiedName.Contains(':') ? qualifiedName[(qualifiedName.IndexOf(':') + 1)..] : qualifiedName;

    private static XElement SchemaRoot() => XDocument.Parse(EmbeddedP60Xsd()).Root!;

    private static XElement DescriptorRoot() => XDocument.Parse(EmbeddedP60Xml()).Root!;

    // Reads an embedded resource of Kape22Importer exactly as the importer will at runtime.
    private static string EmbeddedResource(string logicalName)
    {
        Assembly importer = typeof(P60Deserializer).Assembly;

        using Stream stream = importer.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{logicalName}' is missing from {importer.GetName().Name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static string EmbeddedP60Xsd() => EmbeddedResource(EmbeddedP60XsdResourceName);

    private static string EmbeddedP60Xml() => EmbeddedResource(EmbeddedP60XmlResourceName);

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    private static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));
}
