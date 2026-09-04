using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using static TextToXml.Tests.TestSupport;

namespace TextToXml.Tests;

// Story 1.6 - Champ extraction, Descripteur-driven typing and normalized XML serialization (FR-5).
// TDD: these tests are written before the extraction and serialization stage and its wiring into
// Converter (CC-1). The stage runs only when Errors is empty and produces the deterministic,
// deserializable normalized XML document (AC-FR5-1 to AC-FR5-11, AC-FR5-12a).
// Vocabulary follows the PRD glossary section 3 (Fichier, Ligne, Bloc, Champ...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class NormalizedXmlTests
{
    // KAPE22-like profile: <header> + one <message> + <footer>, Segment control on, one int Champ in
    // the Header (none), two int Champs in the Message (Indice, DiametreProduit) and one in the Footer
    // (Records). Champs are declared in Position order; the last Champ of each Bloc is the trailing
    // Filler / Reserve whose truncation at the end of the Ligne is tolerated.
    private const string HeaderMessageFooter = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="EOF" footerMarker="999">
          <header>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Filler" Position="18" Size="7" datatype="string" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Type" Position="21" Size="1" datatype="string" />
            <value Id="OF" Position="22" Size="7" datatype="string" />
            <value Id="Indice" Position="29" Size="1" datatype="int" />
            <value Id="Client" Position="30" Size="15" datatype="string" />
            <value Id="DiametreProduit" Position="45" Size="7" datatype="int" />
            <value Id="Reserve" Position="52" Size="16" datatype="string" />
          </message>
          <footer>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Records" Position="12" Size="5" datatype="int" />
            <value Id="Filler" Position="17" Size="10" datatype="string" />
          </footer>
        </commande>
        """;

    // Message-only Descripteur with two string Champs; every Ligne becomes a <message> (AC-FR5-2).
    private const string MessageOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Beta" Position="3" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur whose <value> declaration order differs from Position order, to lock
    // that children are emitted in Descripteur declaration order, not in Position order (AC-FR5-11, R-4).
    private const string MessageOnlyDeclarationOrder = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Second" Position="10" Size="3" datatype="string" />
            <value Id="First" Position="0" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single int Champ declared last, so a truncated Ligne can leave it
    // entirely absent (AC-FR5-4, empty element).
    private const string MessageOnlyTrailingInt = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Count" Position="10" Size="7" datatype="int" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single int Champ, exercised with valid and invalid Valeurs brutes.
    private const string MessageOnlySingleInt = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Marker" Position="0" Size="1" datatype="string" />
            <value Id="Amount" Position="1" Size="7" datatype="int" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single string Champ that carries no datatype attribute; it must
    // default to string with TrimEnd (AC-FR5-7).
    private const string MessageOnlyNoDatatype = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Free" Position="0" Size="10" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with two Champs sharing the exact same slice (Position 9, Size 3), like
    // Segment and NumeroFichier of the corrected P60 message (D23); both elements must be emitted (AC-FR5-9).
    private const string MessageOnlyOverlappingChamps = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Head" Position="0" Size="9" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="9" Size="3" datatype="string" />
            <value Id="Tail" Position="12" Size="6" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single string Champ wide enough to carry XML metacharacters,
    // to lock escaping and round-tripping through XDocument.Parse (AC-FR5-8).
    private const string MessageOnlyWideString = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Payload" Position="0" Size="20" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a middle string Champ whose Size overruns a short Ligne, so the
    // clamp of a non-trailing Champ can be exercised (AC-FR5-4).
    private const string MessageOnlyClampedMiddleChamp = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Mid" Position="3" Size="9" datatype="string" />
            <value Id="Tail" Position="20" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with a single int Champ wide enough to carry a value beyond Int32 range.
    private const string MessageOnlyWideInt = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Big" Position="0" Size="12" datatype="int" />
          </message>
        </commande>
        """;

    // The Ids of the <value> Champs declared in a section, in declaration order.
    private static string[] DeclaredIds(string descriptor, string section) =>
        Root(descriptor).Element(section)!.Elements("value")
            .Select(value => (string)value.Attribute("Id")!)
            .ToArray();

    // A three-Ligne KAPE22 Fichier that matches HeaderMessageFooter and normalizes without any Error.
    private static byte[] ReferenceFichier() => Windows1252(string.Join(
        "\r\n",
        Row(25, (0, "P60"), (6, "001"), (9, "000")),
        Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
        Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

    // AC-FR5-1: on a header/message/footer Descripteur the XML is
    // <file><header>...</header><message>...</message><footer>...</footer></file>, each section with
    // one child per Champ, element name equal to the Champ Id, and every <value> emitted.
    [Fact]
    [Trait("AC", "FR5-1")]
    public void Convert_HeaderMessageFooter_EmitsFileWithOneChildPerChamp_AcFr5_1()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        Assert.True(result.Success);
        Assert.NotNull(result.Xml);

        XElement file = FileRoot(result.Xml!);
        Assert.Equal("file", file.Name.LocalName);
        Assert.Equal(new[] { "header", "message", "footer" }, file.Elements().Select(e => e.Name.LocalName));

        Assert.Equal(
            DeclaredIds(HeaderMessageFooter, "header"),
            file.Element("header")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(
            DeclaredIds(HeaderMessageFooter, "message"),
            file.Element("message")!.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(
            DeclaredIds(HeaderMessageFooter, "footer"),
            file.Element("footer")!.Elements().Select(e => e.Name.LocalName));
    }

    // AC-FR5-2: a Descripteur without <header>/<footer> and N Lignes yields <file> with N <message>
    // children and no <header> or <footer>.
    [Fact]
    [Trait("AC", "FR5-2")]
    public void Convert_MessageOnly_FourLignes_EmitsFourMessagesAndNoHeaderOrFooter_AcFr5_2()
    {
        string fichier = string.Join("\r\n", "AAABBB", "CCCDDD", "EEEFFF", "GGGHHH");

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnly);

        Assert.True(result.Success);
        XElement file = FileRoot(result.Xml!);
        Assert.Equal("file", file.Name.LocalName);
        Assert.Equal(4, file.Elements("message").Count());
        Assert.Empty(file.Elements("header"));
        Assert.Empty(file.Elements("footer"));
        Assert.All(file.Elements("message"), message =>
            Assert.Equal(new[] { "Alpha", "Beta" }, message.Elements().Select(e => e.Name.LocalName)));
    }

    // AC-FR5-3: a string Champ "APERAM ALLOYS" plus fixed-width padding is normalized with TrimEnd,
    // internal spaces kept.
    [Fact]
    [Trait("AC", "FR5-3")]
    public void Convert_StringChampWithPadding_IsTrimmedAtEndOnly_AcFr5_3()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal("APERAM ALLOYS", message.Element("Client")!.Value);
    }

    // AC-FR5-3: only the trailing fixed-width space padding is trimmed; leading spaces and a
    // trailing non-space whitespace character (a tab) are kept.
    [Fact]
    [Trait("AC", "FR5-3")]
    public void Convert_StringChamp_TrimsTrailingSpacePaddingOnly_AcFr5_3()
    {
        // Payload @0 Size 20 reads "  abc" then a tab, then space padding to 20.
        string fichier = ("  abc" + '\t').PadRight(20);

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlyWideString);

        Assert.True(result.Success);
        Assert.Equal("  abc\t", FileRoot(result.Xml!).Element("message")!.Element("Payload")!.Value);
    }

    // AC-FR5-4: an int Champ drops leading zeros; "0005900" -> 5900 and "00003" -> 3.
    [Fact]
    [Trait("AC", "FR5-4")]
    public void Convert_IntChamp_DropsLeadingZeros_AcFr5_4()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        XElement file = FileRoot(result.Xml!);
        Assert.Equal("5900", file.Element("message")!.Element("DiametreProduit")!.Value);
        Assert.Equal("1", file.Element("message")!.Element("Indice")!.Value);
        Assert.Equal("3", file.Element("footer")!.Element("Records")!.Value);
    }

    // AC-FR5-4: an all-zero int raw value normalizes to "0".
    [Fact]
    [Trait("AC", "FR5-4")]
    public void Convert_IntChampAllZeros_NormalizesToZero_AcFr5_4()
    {
        string fichier = "X" + "0000000";

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlySingleInt);

        Assert.True(result.Success);
        Assert.Equal("0", FileRoot(result.Xml!).Element("message")!.Element("Amount")!.Value);
    }

    // AC-FR5-4: a blank int raw value yields an empty element (the NOT NULL obligation is judged in Step 2).
    [Fact]
    [Trait("AC", "FR5-4")]
    public void Convert_IntChampBlank_YieldsEmptyElement_AcFr5_4()
    {
        string fichier = "X" + "       ";

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlySingleInt);

        Assert.True(result.Success);
        XElement amount = FileRoot(result.Xml!).Element("message")!.Element("Amount")!;
        Assert.Equal(string.Empty, amount.Value);
    }

    // AC-FR5-4: an int Champ that is the last declared Champ and is entirely absent at the end of the
    // Ligne yields an empty element, not a LineTooShort.
    [Fact]
    [Trait("AC", "FR5-4")]
    public void Convert_TrailingIntChampAbsent_YieldsEmptyElement_AcFr5_4()
    {
        // Ligne of 3 characters: Alpha @0 is covered, the trailing Count @10 is missing.
        ConversionResult result = Converter.Convert(Windows1252("ABC"), MessageOnlyTrailingInt);

        Assert.True(result.Success);
        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal("ABC", message.Element("Alpha")!.Value);
        Assert.Equal(string.Empty, message.Element("Count")!.Value);
    }

    // AC-FR5-4: a non-trailing Champ whose declared Size overruns a short Ligne is emitted with the
    // clamped slice the Ligne actually carries (Story 1.5 only guarantees the starting Position).
    [Fact]
    [Trait("AC", "FR5-4")]
    public void Convert_NonTrailingChampOverrunsLigne_EmitsClampedValue_AcFr5_4()
    {
        // Ligne of 8 characters: Alpha @0/3 -> "ABC", Mid @3/9 clamps to "DEFGH", Tail @20 is absent.
        ConversionResult result = Converter.Convert(Windows1252("ABCDEFGH"), MessageOnlyClampedMiddleChamp);

        Assert.True(result.Success);
        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal("ABC", message.Element("Alpha")!.Value);
        Assert.Equal("DEFGH", message.Element("Mid")!.Value);
        Assert.Equal(string.Empty, message.Element("Tail")!.Value);
    }

    // AC-FR5-5: a non-numeric int raw value yields a single blocking InvalidInteger Error carrying
    // the FieldId and the RawValue, and no XML is produced.
    [Fact]
    [Trait("AC", "FR5-5")]
    public void Convert_IntChampNonNumeric_YieldsInvalidInteger_AcFr5_5()
    {
        string fichier = "X" + "11A0   ";

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlySingleInt);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidInteger, error.Code);
        Assert.Equal("Amount", error.FieldId);
        Assert.Contains("11A0", error.RawValue);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // AC-FR5-5: a signed int raw value is rejected as InvalidInteger (int Champs are always unsigned, D17).
    [Fact]
    [Trait("AC", "FR5-5")]
    public void Convert_IntChampSigned_YieldsInvalidInteger_AcFr5_5()
    {
        string fichier = "X" + "-12    ";

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlySingleInt);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidInteger, error.Code);
        Assert.Equal("Amount", error.FieldId);
        Assert.Contains("-12", error.RawValue);
    }

    // AC-FR5-5: an all-digit int raw value that overruns Int32 range is rejected as InvalidInteger,
    // no XML produced (D17 - the Descripteur type is int).
    [Fact]
    [Trait("AC", "FR5-5")]
    public void Convert_IntChampBeyondInt32Range_YieldsInvalidInteger_AcFr5_5()
    {
        // Big @0 Size 12 reads twelve nines, far past int.MaxValue.
        ConversionResult result = Converter.Convert(Windows1252("999999999999"), MessageOnlyWideInt);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidInteger, error.Code);
        Assert.Equal("Big", error.FieldId);
    }

    // AC-FR5-5: the InvalidInteger Error carries the Bloc and the 1-based LineNumber of the offending
    // Ligne, not only the FieldId.
    [Fact]
    [Trait("AC", "FR5-5")]
    public void Convert_IntChampInvalidOnDetailLigne_ErrorCarriesBlocAndLineNumber_AcFr5_5()
    {
        // Header on Ligne 1, the bad DiametreProduit ("12A4567") on the Detail Ligne 2, Footer on Ligne 3.
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "12A4567")),
            Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidInteger, error.Code);
        Assert.Equal("DiametreProduit", error.FieldId);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal(2, error.LineNumber);
    }

    // AC-FR5-5: every typing Error is collected in one pass; two invalid int Champs on the same Ligne
    // yield two Errors and no XML.
    [Fact]
    [Trait("AC", "FR5-5")]
    public void Convert_SeveralInvalidIntChamps_CollectsEveryError_AcFr5_5()
    {
        // Detail Ligne carries a bad Indice ("Z" @29) and a bad DiametreProduit ("12A4567" @45).
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "Z"), (30, "APERAM ALLOYS"), (45, "12A4567")),
            Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.False(result.Success);
        Assert.Null(result.Xml);
        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal(ErrorCode.InvalidInteger, e.Code));
        Assert.Equal(new[] { "Indice", "DiametreProduit" }, result.Errors.Select(e => e.FieldId).ToArray());
    }

    // AC-FR5-6: a string Champ that is empty or all spaces yields an empty element.
    [Fact]
    [Trait("AC", "FR5-6")]
    public void Convert_StringChampAllSpaces_YieldsEmptyElement_AcFr5_6()
    {
        // Alpha @0 Size 3 reads three spaces, Beta @3 Size 3 reads "XYZ".
        string fichier = "   XYZ";

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnly);

        Assert.True(result.Success);
        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal(string.Empty, message.Element("Alpha")!.Value);
        Assert.Equal("XYZ", message.Element("Beta")!.Value);
    }

    // AC-FR5-7: a Champ with no datatype attribute defaults to string and is normalized with TrimEnd.
    [Fact]
    [Trait("AC", "FR5-7")]
    public void Convert_ChampWithoutDatatype_DefaultsToStringTrimEnd_AcFr5_7()
    {
        ConversionResult result = Converter.Convert(Windows1252("abc       "), MessageOnlyNoDatatype);

        Assert.True(result.Success);
        Assert.Equal("abc", FileRoot(result.Xml!).Element("message")!.Element("Free")!.Value);
    }

    // AC-FR5-8: XML metacharacters in a raw value are escaped and the document reloads through
    // XDocument.Parse with the original value intact.
    [Fact]
    [Trait("AC", "FR5-8")]
    public void Convert_ValueWithXmlMetacharacters_IsEscapedAndReloadable_AcFr5_8()
    {
        // Payload @0 Size 20 carries the raw text "A&B<C>D" then padding.
        ConversionResult result = Converter.Convert(Windows1252("A&B<C>D".PadRight(20)), MessageOnlyWideString);

        Assert.True(result.Success);
        Assert.Contains("&amp;", result.Xml);
        Assert.Contains("&lt;", result.Xml);
        Assert.Contains("&gt;", result.Xml);

        XElement reloaded = FileRoot(result.Xml!);
        Assert.Equal("A&B<C>D", reloaded.Element("message")!.Element("Payload")!.Value);
    }

    // AC-FR5-9: two Champs mapped to the same slice both appear in the XML with the same value.
    [Fact]
    [Trait("AC", "FR5-9")]
    public void Convert_OverlappingChamps_BothElementsEmitted_AcFr5_9()
    {
        string fichier = Row(18, (0, "P60200001"), (9, "EOF"), (12, "652682"));

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlyOverlappingChamps);

        Assert.True(result.Success);
        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal("EOF", message.Element("Segment")!.Value);
        Assert.Equal("EOF", message.Element("NumeroFichier")!.Value);
    }

    // AC-FR5-10: the produced XML has no BOM and opens with the utf-8 declaration.
    [Fact]
    [Trait("AC", "FR5-10")]
    public void Convert_ProducedXml_HasNoBomAndUtf8Declaration_AcFr5_10()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        Assert.NotNull(result.Xml);

        // A leading UTF-8 BOM (U+FEFF) would push the '<' off index 0; the declaration must come first.
        Assert.Equal('<', result.Xml![0]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result.Xml);
    }

    // AC-FR5-11: two conversions of the same input produce byte-for-byte identical XML.
    [Fact]
    [Trait("AC", "FR5-11")]
    public void Convert_CalledTwice_ProducesIdenticalXml_AcFr5_11()
    {
        string? first = Converter.Convert(ReferenceFichier(), HeaderMessageFooter).Xml;
        string? second = Converter.Convert(ReferenceFichier(), HeaderMessageFooter).Xml;

        Assert.NotNull(first);
        Assert.Equal(first, second, StringComparer.Ordinal);
    }

    // AC-FR5-11: children are emitted in the Descripteur <value> declaration order, not in Position order.
    [Fact]
    [Trait("AC", "FR5-11")]
    public void Convert_ChildrenFollowDescriptorDeclarationOrder_AcFr5_11()
    {
        // Second is declared first though it sits at Position 10; First is declared last at Position 0.
        string fichier = Row(13, (0, "AAA"), (10, "BBB"));

        ConversionResult result = Converter.Convert(Windows1252(fichier), MessageOnlyDeclarationOrder);

        Assert.True(result.Success);
        XElement message = FileRoot(result.Xml!).Element("message")!;
        Assert.Equal(new[] { "Second", "First" }, message.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("BBB", message.Element("Second")!.Value);
        Assert.Equal("AAA", message.Element("First")!.Value);
    }

    // AC-FR5-11: the XML is produced as soon as Errors is empty, so a Segment Warning alone does not
    // stop it.
    [Fact]
    [Trait("AC", "FR5-11")]
    public void Convert_SegmentWarningOnly_StillProducesXml_AcFr5_11()
    {
        // Detail Segment reads "000" while the messageMarker is "EOF"; the count is still correct.
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "000"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
            Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.True(result.Success);
        Assert.NotNull(result.Xml);
        Assert.Single(result.Warnings, warning => warning.Code == ErrorCode.SegmentMismatch);
    }

    // AC-FR5-12a: the normalized XML of a valid Fichier deserializes into a DTO record with [XmlElement]
    // without a custom converter; int and string values survive the round-trip.
    [Fact]
    [Trait("AC", "FR5-12a")]
    public void Convert_NormalizedXml_RoundTripsToRecordDto_AcFr5_12a()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        Assert.True(result.Success);

        XmlSerializer serializer = new(typeof(RoundTripFile));
        using StringReader reader = new(result.Xml!);
        RoundTripFile dto = (RoundTripFile)serializer.Deserialize(reader)!;

        Assert.Equal("APERAM ALLOYS", dto.Message.Client);
        Assert.Equal(5900, dto.Message.DiametreProduit);
        Assert.Equal(1, dto.Message.Indice);
    }

    // Minimal DTO for the AC-FR5-12a round-trip. Properties are alphabetical (CC-4); the generated
    // Kape22File variant (AC-FR5-12b) and the mixed decimal/datetime round-trip (CTR-3) are Story 2.3
    // and Story 1.8.
    [XmlRoot("file")]
    public sealed record RoundTripFile
    {
        [XmlElement("message")]
        public RoundTripMessage Message { get; set; } = new();
    }

    // Properties are declared in alphabetical order (CC-4).
    public sealed record RoundTripMessage
    {
        [XmlElement("Client")]
        public string Client { get; set; } = string.Empty;

        [XmlElement("DiametreProduit")]
        public int DiametreProduit { get; set; }

        [XmlElement("Indice")]
        public int Indice { get; set; }
    }
}
