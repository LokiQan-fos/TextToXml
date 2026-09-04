using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.2: the P60 Descripteur gains one datatype per <value> (derived from the L_D_KAPE22 column
// type), the expectedMessageCount and Segment-control attributes, and is embedded as a resource in
// Kape22Importer so TextToXml can type the Champs without knowing P60 (AR-5, D3, D5, D6). These tests
// read the built EF model and the embedded resource only, so they need no database and stay in the
// Unit category. They were written test-first: every assertion here fails red against the
// pre-enrichment P60.xml, which carries no datatype and no expectedMessageCount, and turns green once
// the enriched, embedded descriptor ships.
[Trait("Category", TestCategory.Unit)]
[Trait("AC", "2.2")]
public class P60DescriptorTests
{
    // The only two datatypes the P60 Descripteur is allowed to carry in v1 (D6): no datetime, no decimal.
    private static readonly string[] AllowedDatatypes = ["int", "string"];

    // The LogicalName pinned by Kape22Importer.csproj for the embedded Descripteur.
    private const string EmbeddedP60ResourceName = "Kape22Importer.Templates.P60.xml";

    // The ten Annexe A.4 reference Fichiers, the same set ValidFixturesTests copies into the test output.
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
    public void P60Xml_IsEmbeddedInTheImporterAssembly()
    {
        Assembly importer = typeof(AscoLsiDbContext).Assembly;

        Assert.Contains(EmbeddedP60ResourceName, importer.GetManifestResourceNames());

        using Stream? stream = importer.GetManifestResourceStream(EmbeddedP60ResourceName);
        Assert.NotNull(stream);
        using StreamReader reader = new(stream);
        string content = reader.ReadToEnd();

        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Equal("commande", XDocument.Parse(content).Root!.Name.LocalName);
    }

    // The enriched Descripteur must take every Annexe A.4 reference Fichier through Converter.Convert end
    // to end, not merely avoid a layout Error. A wrong datatype, a wrong Segment marker or a lost slice
    // surfaces here as a typing Error or a SegmentMismatch Warning rather than as LayoutInvalid.
    [Theory]
    [MemberData(nameof(ReferenceFichierNames))]
    public void P60Xml_ConvertsEveryReferenceFichierWithoutErrorOrWarning(string fichierName)
    {
        ConversionResult result = Converter.Convert(ReadValidFixture(fichierName), EmbeddedP60Xml());

        Assert.True(
            result.Errors.Count == 0,
            $"{fichierName}: {string.Join("; ", result.Errors.Select(entry => $"{entry.Code}/{entry.FieldId}"))}");
        Assert.True(
            result.Warnings.Count == 0,
            $"{fichierName}: {string.Join("; ", result.Warnings.Select(entry => $"{entry.Code}/{entry.FieldId}"))}");
        Assert.NotNull(result.Xml);
    }

    [Fact]
    public void P60Xml_RootDeclaresExpectedMessageCountOne()
    {
        XElement root = XDocument.Parse(EmbeddedP60Xml()).Root!;

        Assert.Equal("1", (string?)root.Attribute("expectedMessageCount"));
    }

    [Fact]
    public void P60Xml_DeclaresTheSegmentControlWithThe000EofAnd999Markers()
    {
        XElement root = XDocument.Parse(EmbeddedP60Xml()).Root!;

        Assert.Equal("Segment", (string?)root.Attribute("segmentField"));
        Assert.Equal("000", (string?)root.Attribute("headerMarker"));
        Assert.Equal("EOF", (string?)root.Attribute("messageMarker"));
        Assert.Equal("999", (string?)root.Attribute("footerMarker"));
    }

    [Fact]
    public void P60Xml_SegmentControlIsActiveThroughConverter()
    {
        // Overwrite the header Segment slice (Position 9, Size 3) so the marker no longer matches.
        byte[] input = ReadValidFixture("P60_847_682_001");
        input[9] = input[10] = input[11] = (byte)'Z';

        ConversionResult result = Converter.Convert(input, EmbeddedP60Xml());

        Assert.Contains(result.Warnings, warning =>
            warning.Code == ErrorCode.SegmentMismatch && warning.FieldId == "Segment");
    }

    [Fact]
    public void P60Xml_EveryValueCarriesADatatypeOfStringOrInt()
    {
        List<string> offenders = ValueElements()
            .Where(value => !AllowedDatatypes.Contains((string?)value.Attribute("datatype")))
            .Select(value => (string?)value.Attribute("Id") ?? "(no Id)")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"<value> without a string/int datatype: {string.Join(", ", offenders)}.");
    }

    [Fact]
    public void P60Xml_HasNoDatetimeNoDecimalAndNoConvertAttribute()
    {
        Assert.DoesNotContain(ValueElements(), value =>
            (string?)value.Attribute("datatype") is "datetime" or "decimal");

        Assert.DoesNotContain(ValueElements(), value => value.Attribute("convert") is not null);
    }

    [Fact]
    public void P60Xml_IntDatatypeMatchesTheL_D_KAPE22IntColumns()
    {
        HashSet<string> intColumns = Kape22IntColumns();

        List<string> mismatches = [];
        foreach (XElement value in MessageValueElements())
        {
            string id = (string)value.Attribute("Id")!;
            bool declaredInt = (string?)value.Attribute("datatype") == "int";
            bool columnIsInt = intColumns.Contains(id);

            if (declaredInt != columnIsInt)
            {
                mismatches.Add($"{id}: datatype int={declaredInt}, L_D_KAPE22 column int={columnIsInt}.");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    // Every Bloc must tile its span with no gap and no backwards jump, so the enrichment sweep cannot
    // silently drop, duplicate or shift a slice while keeping the final offset intact. The message Bloc
    // ends exactly at the described length and nothing is declared beyond it (PRD D5); the header and
    // footer records are 80 characters.
    [Theory]
    [InlineData("header", 80)]
    [InlineData("message", 526)]
    [InlineData("footer", 80)]
    public void P60Xml_EachBlocTilesItsSpanContiguously(string blocName, int expectedLength)
    {
        XElement bloc = XDocument.Parse(EmbeddedP60Xml()).Root!.Element(blocName)!;

        int cursor = 0;
        foreach (XElement value in bloc.Elements("value"))
        {
            int position = (int)value.Attribute("Position")!;
            Assert.True(
                position == cursor,
                $"{blocName}/{(string?)value.Attribute("Id")}: starts at {position}, expected {cursor}.");
            cursor += (int)value.Attribute("Size")!;
        }

        Assert.Equal(expectedLength, cursor);
    }

    // Reads the embedded P60 Descripteur exactly as the importer will at runtime, without disk access.
    private static string EmbeddedP60Xml()
    {
        Assembly importer = typeof(AscoLsiDbContext).Assembly;

        using Stream stream = importer.GetManifestResourceStream(EmbeddedP60ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{EmbeddedP60ResourceName}' is missing from {importer.GetName().Name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    // The int columns of L_D_KAPE22, taken from the built EF model exposed by Story 2.1, excluding the
    // identity Id which is not a Champ.
    private static HashSet<string> Kape22IntColumns()
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);
        IEntityType? entity = context.Model.FindEntityType(typeof(L_D_KAPE22));
        Assert.NotNull(entity);

        return entity.GetProperties()
            .Where(property => property.Name != "Id")
            .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(int))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    private static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));

    // Every <value> of the descriptor, across header, message and footer.
    private static IEnumerable<XElement> ValueElements() =>
        XDocument.Parse(EmbeddedP60Xml()).Root!.Descendants("value");

    // Only the <message> Bloc <value>s, the ones that map to an L_D_KAPE22 column.
    private static IEnumerable<XElement> MessageValueElements() =>
        XDocument.Parse(EmbeddedP60Xml()).Root!.Element("message")!.Elements("value");
}
