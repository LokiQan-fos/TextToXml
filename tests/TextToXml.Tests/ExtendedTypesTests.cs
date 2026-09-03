using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace TextToXml.Tests;

// Story 1.8 - Extended datatypes: TextToXml normalizes decimal (decimalSeparator) and datetime
// (convert) Champs in Step 1 (CTR-1, CTR-2) and the resulting XML round-trips into a typed record DTO
// with no custom converter (CTR-3).
// TDD: written before the decimal/datetime branches of NormalizedXmlBuilder (CC-1). The canonical-value
// assertions here were red until those branches landed.
// Contract locked with the product owner: the normalized XML carries ISO-8601 datetime values
// (yyyy-MM-dd when the convert mask has no time token, yyyy-MM-ddTHH:mm:ss otherwise); the French
// display format is an Étape 2 concern. Decimal parsing is driven by decimalSeparator only; the
// canonical form drops trailing zeros the way the int form drops leading zeros, and a leading sign is
// allowed.
// Vocabulary follows the PRD glossary section 3 (Champ, Valeur brute, Valeur normalisée) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class ExtendedTypesTests
{
    private static string ReadDescriptor(string name) =>
        File.ReadAllText(Path.Combine(RepoLayout.FixturesDirectory, "generic", name));

    private static byte[] ReadInput(string name) =>
        File.ReadAllBytes(Path.Combine(RepoLayout.FixturesDirectory, "generic", name));

    // Message-only Descripteur with a single decimal Champ, used for the separator, sign and
    // trailing-zero edge cases that do not need a full fixture file.
    private const string DecimalOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="DEC" format="Fixed">
          <message type="DEC" index="0">
            <value Id="Amount" Position="0" Size="10" datatype="decimal" decimalSeparator="," />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single datetime Champ that carries no convert attribute.
    private const string DatetimeWithoutConvert = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="DT" format="Fixed">
          <message type="DT" index="0">
            <value Id="Stamp" Position="0" Size="6" datatype="datetime" />
          </message>
        </commande>
        """;

    private static XElement Message(string xml) => XDocument.Parse(xml).Root!.Element("message")!;

    // Encodes ASCII-only test text to bytes; the Windows-1252 decoder reads these back unchanged.
    private static byte[] Ascii(string text) => text.Select(c => (byte)c).ToArray();

    // CTR-1: a valid decimal Valeur brute is normalized to its canonical form; the decimalSeparator of
    // the Descripteur ("," here) is honored and the XML carries the invariant "." form.
    [Fact]
    [Trait("AC", "CTR-1")]
    public void Convert_ValidDecimalChamp_EmitsCanonicalInvariantValue_Ctr1()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-valid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.True(result.Success);
        Assert.Equal("123.45", Message(result.Xml!).Element("Amount")!.Value);
    }

    // CTR-1: a decimal Valeur brute that is not a number yields a single blocking InvalidDecimal Error
    // carrying the FieldId and the RawValue, and no XML is produced.
    [Fact]
    [Trait("AC", "CTR-1")]
    public void Convert_InvalidDecimalChamp_YieldsInvalidDecimalAndNoXml_Ctr1()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-invalid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.False(result.Success);
        Assert.Null(result.Xml);

        ConversionError error = Assert.Single(result.Errors, e => e.Code == ErrorCode.InvalidDecimal);
        Assert.Equal("Amount", error.FieldId);
        Assert.Contains("12,3,45", error.RawValue);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // CTR-1: the canonical decimal form drops trailing zeros, so a padded "0012,50" normalizes to "12.5".
    [Fact]
    [Trait("AC", "CTR-1")]
    public void Convert_DecimalChampWithTrailingZeros_CanonicalFormDropsThem_Ctr1()
    {
        ConversionResult result = Converter.Convert(Ascii("0012,50   "), DecimalOnly);

        Assert.True(result.Success);
        Assert.Equal("12.5", Message(result.Xml!).Element("Amount")!.Value);
    }

    // CTR-1: a leading sign is allowed for decimal; "-1,5" normalizes to the invariant "-1.5".
    [Fact]
    [Trait("AC", "CTR-1")]
    public void Convert_SignedDecimalChamp_EmitsCanonicalValue_Ctr1()
    {
        ConversionResult result = Converter.Convert(Ascii("-1,5      "), DecimalOnly);

        Assert.True(result.Success);
        Assert.Equal("-1.5", Message(result.Xml!).Element("Amount")!.Value);
    }

    // CTR-1: a value using a separator other than the one declared by the Descripteur ("," here) is
    // rejected as InvalidDecimal rather than parsed to a plausible wrong number.
    [Fact]
    [Trait("AC", "CTR-1")]
    public void Convert_DecimalChampWithWrongSeparator_YieldsInvalidDecimal_Ctr1()
    {
        ConversionResult result = Converter.Convert(Ascii("12.5      "), DecimalOnly);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidDecimal, error.Code);
        Assert.Equal("Amount", error.FieldId);
    }

    // CTR-2: a datetime Champ declared with no convert attribute is a layout error, reported once as
    // LayoutInvalid before any input is read, not as a runtime InvalidDate.
    [Fact]
    [Trait("AC", "CTR-2")]
    public void Convert_DatetimeChampWithoutConvert_YieldsLayoutInvalid_Ctr2()
    {
        ConversionResult result = Converter.Convert(Ascii("160924"), DatetimeWithoutConvert);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.LayoutInvalid, error.Code);
        Assert.Equal(Block.File, error.Block);
        Assert.Contains("Stamp", error.Message);
    }

    // CTR-2: a valid datetime Valeur brute whose convert mask has no time token is normalized to an
    // ISO-8601 date (yyyy-MM-dd).
    [Fact]
    [Trait("AC", "CTR-2")]
    public void Convert_ValidDatetimeChamp_DateOnlyMask_EmitsIso8601Date_Ctr2()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-valid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.True(result.Success);
        Assert.Equal("2024-09-16", Message(result.Xml!).Element("RecordedOn")!.Value);
    }

    // CTR-2: a valid datetime Valeur brute whose convert mask carries a time token is normalized to an
    // ISO-8601 date and time (yyyy-MM-ddTHH:mm:ss).
    [Fact]
    [Trait("AC", "CTR-2")]
    public void Convert_ValidDatetimeChamp_TimeMask_EmitsIso8601DateTime_Ctr2()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-valid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.True(result.Success);
        Assert.Equal("2024-09-16T08:30:00", Message(result.Xml!).Element("SentAt")!.Value);
    }

    // CTR-2: a datetime Valeur brute that does not match its convert mask yields a single blocking
    // InvalidDate Error carrying the FieldId and the RawValue, and no XML is produced.
    [Fact]
    [Trait("AC", "CTR-2")]
    public void Convert_InvalidDatetimeChamp_YieldsInvalidDateAndNoXml_Ctr2()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-invalid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.False(result.Success);
        Assert.Null(result.Xml);

        ConversionError error = Assert.Single(result.Errors, e => e.Code == ErrorCode.InvalidDate);
        Assert.Equal("RecordedOn", error.FieldId);
        Assert.Contains("319999", error.RawValue);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // CTR-1, CTR-2: every typing Error is collected in one pass; the invalid fixture carries a bad
    // decimal and a bad datetime and yields exactly those two Errors, in Descripteur declaration order.
    [Fact]
    [Trait("AC", "CTR-1")]
    [Trait("AC", "CTR-2")]
    public void Convert_InvalidTypedFixture_CollectsBothErrorsInDeclarationOrder_Ctr1_Ctr2()
    {
        ConversionResult result = Converter.Convert(ReadInput("typed-values-invalid.txt"), ReadDescriptor("typed-values.xml"));

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        Assert.Equal(
            new[] { ErrorCode.InvalidDecimal, ErrorCode.InvalidDate },
            result.Errors.Select(e => e.Code).ToArray());
        Assert.Equal(new[] { "Amount", "RecordedOn" }, result.Errors.Select(e => e.FieldId).ToArray());
    }

    // CTR-3: the normalized XML of a valid mixed-type Fichier deserializes into a record DTO with
    // [XmlElement] and no custom converter; string, int, decimal and DateTime values all survive.
    [Fact]
    [Trait("AC", "CTR-3")]
    public void Convert_MixedTypedXml_RoundTripsToRecordDto_Ctr3()
    {
        ConversionResult result = Converter.Convert(ReadInput("roundtrip.txt"), ReadDescriptor("roundtrip.xml"));

        Assert.True(result.Success);

        XmlSerializer serializer = new(typeof(MixedRoundTripFile));
        using StringReader reader = new(result.Xml!);
        MixedRoundTripFile dto = (MixedRoundTripFile)serializer.Deserialize(reader)!;

        Assert.Equal("ABCD", dto.Message.Code);
        Assert.Equal(42, dto.Message.Count);
        Assert.Equal(12.50m, dto.Message.Price);
        Assert.Equal(new DateTime(2024, 9, 16), dto.Message.MadeOn);
    }

    // DTO for the CTR-3 round-trip. Properties are declared in alphabetical order (CC-4).
    [XmlRoot("file")]
    public sealed record MixedRoundTripFile
    {
        [XmlElement("message")]
        public MixedRoundTripMessage Message { get; set; } = new();
    }

    // Properties are declared in alphabetical order (CC-4).
    public sealed record MixedRoundTripMessage
    {
        [XmlElement("Code")]
        public string Code { get; set; } = string.Empty;

        [XmlElement("Count")]
        public int Count { get; set; }

        [XmlElement("MadeOn")]
        public DateTime MadeOn { get; set; }

        [XmlElement("Price")]
        public decimal Price { get; set; }
    }
}
