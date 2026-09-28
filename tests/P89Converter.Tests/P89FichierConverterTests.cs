using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using TextToXml.Tests;
using static P89Converter.Tests.TestSupport;

namespace P89Converter.Tests;

// One P89 Fichier through the Step 1 pipeline (Story 5.1): strict UTF-8 -> Windows-1252 transcode (D29),
// Converter.Convert with the embedded Descripteur, validation against the embedded P89.xsd. Written
// test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public class P89FichierConverterTests
{
    public static TheoryData<string> ReferenceFichiers() => [.. ReferenceFichierNames];

    // AC-FR22-1: every reference Fichier, the accented one included, converts without Error into a
    // normalized XML valid against P89.xsd, with its NumeroFichier and OF readable.
    [Theory]
    [MemberData(nameof(ReferenceFichiers))]
    [Trait("AC", "FR22-1")]
    public void Convert_ReferenceFichier_YieldsSchemaValidXml_AcFr22_1(string fichierName)
    {
        P89Conversion conversion = P89FichierConverter.Convert(ReadFixture(fichierName));

        Assert.True(conversion.Success, string.Join(Environment.NewLine, conversion.Reasons));
        Assert.NotNull(conversion.Xml);
        Assert.False(string.IsNullOrWhiteSpace(conversion.NumeroFichier));
        Assert.False(string.IsNullOrWhiteSpace(conversion.OF));
    }

    // AC-FR22-1 / D29: the accented character survives the transcode into the normalized XML.
    [Fact]
    [Trait("AC", "FR22-1")]
    public void Convert_AccentedFichier_KeepsTheAccentInTheXml_AcFr22_1()
    {
        P89Conversion conversion = P89FichierConverter.Convert(ReadFixture(AccentedFichierName));

        Assert.Contains("é", conversion.Xml, StringComparison.Ordinal);
    }

    // D8: the log row carries the raw, zero-padded values, not the normalized int (NumeroFichier "013").
    [Fact]
    [Trait("AC", "FR22-5")]
    public void Convert_AccentedFichier_KeepsTheRawNumeroFichierAndOf_AcFr22_5()
    {
        P89Conversion conversion = P89FichierConverter.Convert(ReadFixture(AccentedFichierName));

        Assert.Equal("013", conversion.NumeroFichier);
        Assert.Equal("2039841", conversion.OF);
    }

    // AC-FR22-3: an invalid UTF-8 byte fails the Fichier with an encoding reason; no XML, so no OF.
    [Fact]
    [Trait("AC", "FR22-3")]
    public void Convert_InvalidUtf8Byte_FailsWithAnEncodingReason_AcFr22_3()
    {
        P89Conversion conversion = P89FichierConverter.Convert(InvalidUtf8Fichier());

        Assert.False(conversion.Success);
        Assert.Null(conversion.Xml);
        Assert.Null(conversion.OF);
        Assert.StartsWith("Encodage", Assert.Single(conversion.Reasons), StringComparison.Ordinal);
    }

    // AC-FR22-3: a character absent from Windows-1252 fails the Fichier instead of being replaced.
    [Fact]
    [Trait("AC", "FR22-3")]
    public void Convert_CharacterOutsideWindows1252_FailsWithAnEncodingReason_AcFr22_3()
    {
        P89Conversion conversion = P89FichierConverter.Convert(OutsideWindows1252Fichier());

        Assert.False(conversion.Success);
        Assert.Null(conversion.Xml);
        Assert.StartsWith("Encodage", Assert.Single(conversion.Reasons), StringComparison.Ordinal);
    }

    // AC-FR22-6: a truncated Ligne fails with the Converter's Errors; no XML, so the OF is unreadable.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void Convert_TruncatedLigne_FailsWithConversionErrors_AcFr22_6()
    {
        P89Conversion conversion = P89FichierConverter.Convert(TruncatedFichier());

        Assert.False(conversion.Success);
        Assert.Null(conversion.Xml);
        Assert.Null(conversion.OF);
        Assert.NotEmpty(conversion.Reasons);
    }

    // AC-FR22-6: a normalized XML the schema rejects fails with an XSD reason but keeps its OF readable,
    // so the REJETÉ L_D_LOG_COMMANDE row can be written. A stricter schema stands in for a schema error
    // no raw Fichier can produce.
    [Fact]
    [Trait("AC", "FR22-6")]
    public void Convert_SchemaInvalidXml_FailsWithAnXsdReasonAndKeepsTheOf_AcFr22_6()
    {
        P89Conversion conversion = P89FichierConverter.Convert(ReadFixture(AccentedFichierName), StricterSchema());

        Assert.False(conversion.Success);
        Assert.StartsWith("XSD", conversion.Reasons.First(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(conversion.OF));
    }

    // P89.xsd with a required element no normalized XML carries, prepended to the message sequence.
    internal static XmlSchemaSet StricterSchema()
    {
        string xsd = Regex.Replace(
            P89Templates.SchemaXsd,
            @"(<xs:complexType name=""message"">\s*<xs:sequence>)",
            "$1<xs:element name=\"Bogus\" type=\"xs:string\" />");
        Assert.Contains("Bogus", xsd, StringComparison.Ordinal);

        XmlSchemaSet schemas = new();
        using XmlReader reader = XmlReader.Create(new StringReader(xsd));
        schemas.Add(null, reader);
        return schemas;
    }
}
