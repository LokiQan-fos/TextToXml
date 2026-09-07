using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.4: Kape22Mapper.Map(normalizedXml, sourceFileName) turns the Kape22File DTO produced by
// P60Deserializer (Story 2.3) into an insertable L_D_KAPE22 entity per PRD Annexe B. Written test-first
// (CC-1): red until Kape22Mapper ships. Unit-only (AR-12), no database needed.
[Trait("Category", TestCategory.Unit)]
public class Kape22MapperTests
{
    private const string EmbeddedP60XmlResourceName = "Kape22Importer.Templates.P60.xml";

    private const string ReferenceFichierName = "P60_847_682_001";

    // AC-FR7-2: every homonymous Detail property lands on the entity with the DTO's typed value.
    // Spot-checks OF/Client/Coulee/Nuance/Type/Indice directly against the normalized XML (ground
    // truth, independent of Kape22Mapper's own reflection), then sweeps every other mapped property.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_ValidFichier_CopiesEveryHomonymousDetailProperty_AcFr7_2()
    {
        string normalizedXml = ConvertReferenceFichier();
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(normalizedXml, ReferenceFichierName);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        L_D_KAPE22 entity = result.Value!;

        XElement message = XDocument.Parse(normalizedXml).Root!.Element("message")!;
        Assert.Equal((string)message.Element("OF")!, entity.OF);
        Assert.Equal((string)message.Element("Client")!, entity.Client);
        Assert.Equal((string)message.Element("Coulee")!, entity.Coulee);
        Assert.Equal((string)message.Element("Nuance")!, entity.Nuance);
        Assert.Equal((string)message.Element("Type")!, entity.Type);
        Assert.Equal(int.Parse((string)message.Element("Indice")!, CultureInfo.InvariantCulture), entity.Indice);

        Kape22FileMessage dtoMessage = P60Deserializer.Deserialize(normalizedXml).File!.Message;
        foreach (PropertyInfo source in typeof(Kape22FileMessage).GetProperties())
        {
            if (Kape22Mapper.IsIgnored(source.Name))
            {
                continue;
            }

            string targetName = Kape22Mapper.ResolveTargetName(source.Name);
            PropertyInfo target = typeof(L_D_KAPE22).GetProperty(
                targetName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

            object? expected = source.GetValue(dtoMessage) ?? Kape22Mapper.DefaultForNonNullable(target.PropertyType);
            object? actual = target.GetValue(entity);
            Assert.True(
                Equals(expected, actual),
                $"{source.Name} -> {target.Name}: expected {expected ?? "null"}, got {actual ?? "null"}.");
        }
    }

    // AC-FR7-4: the one Annexe B naming exception (DTO OFOriginInterne -> entity OForiginInterne) is applied.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void Map_OFOriginInterneChamp_LandsOnEntityOForiginInterne_AcFr7_4()
    {
        string normalizedXml = ConvertReferenceFichier();
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(normalizedXml, ReferenceFichierName);

        XElement message = XDocument.Parse(normalizedXml).Root!.Element("message")!;
        string expected = (string)message.Element("OForiginInterne")!;

        Assert.True(result.Success);
        Assert.Equal(expected, result.Value!.OForiginInterne);
    }

    // AC-FR7-4: proves ResolveTargetName actually substitutes the mapped name (case-sensitive check),
    // independent of Kape22FileMessage/L_D_KAPE22's real property casing, which happens to already
    // match case-insensitively and so cannot on its own prove the exception mechanism is exercised.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void ResolveTargetName_MappedSourceName_SubstitutesDictionaryTarget_AcFr7_4()
    {
        Assert.Equal("OForiginInterne", Kape22Mapper.ResolveTargetName("OFOriginInterne"));
        Assert.NotEqual("OFOriginInterne", Kape22Mapper.ResolveTargetName("OFOriginInterne"), StringComparer.Ordinal);
    }

    // AC-FR7-4: an unmapped source name falls back to itself unchanged.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void ResolveTargetName_UnmappedSourceName_ReturnsInputUnchanged_AcFr7_4()
    {
        Assert.Equal("SomeUnmappedProperty", Kape22Mapper.ResolveTargetName("SomeUnmappedProperty"));
    }

    // AC-FR7-5: Annexe B's ignored Detail properties are not copied anywhere, and Map raises no error.
    [Theory]
    [InlineData("Element")]
    [InlineData("KAP")]
    [InlineData("Segment")]
    [InlineData("Date")]
    [InlineData("Reserve")]
    [InlineData("ReserveSVT")]
    [InlineData("ReserveRefroidissoir")]
    [Trait("AC", "FR7-5")]
    public void Map_IgnoredDetailProperty_IsInTheIgnoredSet_AcFr7_5(string dtoPropertyName)
    {
        Assert.True(Kape22Mapper.IsIgnored(dtoPropertyName));
    }

    [Fact]
    [Trait("AC", "FR7-5")]
    public void Map_ValidFichier_RaisesNoErrorDespiteIgnoredProperties_AcFr7_5()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    // AC-FR7-6: each Annexe B naming-exception entry names a source member that exists on
    // Kape22FileMessage and a target member that exists on L_D_KAPE22 (both by reflection).
    public static TheoryData<string, string> NamingExceptionEntries()
    {
        TheoryData<string, string> data = new();
        foreach ((string source, string target) in Kape22Mapper.NamingExceptions)
        {
            data.Add(source, target);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NamingExceptionEntries))]
    [Trait("AC", "FR7-6")]
    public void Map_NamingExceptionEntry_SourceAndTargetMembersExist_AcFr7_6(string sourceName, string targetName)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        Assert.True(
            typeof(Kape22FileMessage).GetProperty(sourceName, Flags) is not null,
            $"Kape22FileMessage has no property named '{sourceName}'.");
        Assert.True(
            typeof(L_D_KAPE22).GetProperty(targetName, Flags) is not null,
            $"L_D_KAPE22 has no property named '{targetName}'.");
    }

    // Edge case: a blank Indice (element omitted from the normalized XML) does not throw, and the
    // non-nullable entity Indice is left at its default (0). No error is raised here (Story 2.6/FR-9 owns
    // RequiredFieldMissing-style validation).
    [Fact]
    public void Map_BlankIndice_DoesNotThrowAndDefaultsToZero()
    {
        string withoutIndice = Regex.Replace(ConvertReferenceFichier(), "<Indice>[^<]*</Indice>", string.Empty);

        MapResult<L_D_KAPE22>? result = null;
        Exception? exception = Record.Exception(() => result = Kape22Mapper.Map(withoutIndice, ReferenceFichierName));

        Assert.Null(exception);
        Assert.True(result!.Success);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Value!.Indice);
    }

    // Map's deserialization-failure passthrough: when P60Deserializer.Deserialize rejects the normalized
    // XML (schema violation, deserialized.File is null), Map returns early with the same Errors and no
    // Value, instead of throwing or mapping a partial entity.
    [Fact]
    public void Map_NonConformantNormalizedXml_ReturnsDeserializerErrorsAndNoValue()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(NonConformantNormalizedXml(), ReferenceFichierName);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.PersistenceError, error.Code);
    }

    private static string ConvertReferenceFichier()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture(ReferenceFichierName), EmbeddedP60Xml());
        Assert.True(conversion.Success, "reference fixture failed to convert.");
        return conversion.Xml!;
    }

    // Builds a normalized XML that P60.xsd rejects by inserting an element the schema does not declare
    // as the first child of <message>. Mirrors P60XsdTests.NonConformantNormalizedXml.
    private static string NonConformantNormalizedXml() =>
        ConvertReferenceFichier().Replace("<message>", "<message><Bogus>x</Bogus>", StringComparison.Ordinal);

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

    private static string EmbeddedP60Xml() => EmbeddedResource(EmbeddedP60XmlResourceName);

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    private static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));
}
