using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TextToXml.Tests;

// Story 1.5 - Ligne length control for format="Fixed" (FR-4).
// TDD: these tests are written before LineLengthChecker and its wiring into Converter (CC-1).
// Vocabulary follows the PRD glossary section 3 (Fichier, Ligne, Bloc, Champ, Position...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class LineLengthTests
{
    // KAPE22-like profile after Annexe A: <header> + one <message> + <footer>, Segment control on.
    // Each Bloc declares its Champs in Position order; the last Champ of each Bloc is the trailing
    // Filler / Reserve whose truncation or total absence at the end of the Ligne is tolerated (AC-FR4-1).
    private const string Kape22Profile = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="EOF" footerMarker="999">
          <header>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Emet" Position="12" Size="3" datatype="string" />
            <value Id="Recepteur" Position="15" Size="3" datatype="string" />
            <value Id="Filler" Position="18" Size="62" datatype="string" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Type" Position="21" Size="1" datatype="string" />
            <value Id="OF" Position="22" Size="7" datatype="string" />
            <value Id="Reserve" Position="510" Size="16" datatype="string" />
          </message>
          <footer>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Records" Position="12" Size="5" datatype="int" />
            <value Id="Filler" Position="17" Size="63" datatype="string" />
          </footer>
        </commande>
        """;

    // Message-only Descripteur with three non-trailing Champs and one trailing Champ (Tail). Used to
    // exercise a multi-Ligne Detail Bloc and the "one Error per Ligne, not per Champ" rule (AC-FR4-6).
    private const string MessageOnlyProfile = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Beta" Position="10" Size="3" datatype="string" />
            <value Id="Gamma" Position="20" Size="3" datatype="string" />
            <value Id="Tail" Position="30" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur whose last declared Champ (Trailer) sits far down the Ligne. Used to
    // lock that the exemption follows declaration order, whatever Position the last Champ carries.
    private const string DetailWithFarTrailingChamp = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Beta" Position="10" Size="3" datatype="string" />
            <value Id="Trailer" Position="200" Size="16" datatype="string" />
          </message>
        </commande>
        """;

    // Fixture Descripteur matching detail_too_short.txt (Annexe A.4): Detail Champ Type @21 is not
    // reached by the truncated Detail Ligne, while Header and Footer Lignes are long enough.
    private const string Kape22FixtureProfile = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="EOF" footerMarker="999">
          <header>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Filler" Position="12" Size="60" datatype="string" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Type" Position="21" Size="1" datatype="string" />
            <value Id="OF" Position="22" Size="7" datatype="string" />
            <value Id="Reserve" Position="60" Size="16" datatype="string" />
          </message>
          <footer>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Records" Position="12" Size="5" datatype="int" />
            <value Id="Filler" Position="17" Size="63" datatype="string" />
          </footer>
        </commande>
        """;

    private static XElement Root(string descriptor) => XDocument.Parse(descriptor).Root!;

    // Encodes ASCII-only test text to Windows-1252 bytes; a plain byte cast is enough for these callers.
    private static byte[] Windows1252(string text) => text.Select(c => (byte)c).ToArray();

    // Pads a prefix with 'X' up to the requested Ligne length; the Segment markers stay where declared.
    private static string Line(string prefix, int length) => prefix.PadRight(length, 'X');

    // Reads one of the faulty fixture files that live directly under fixtures/ (Annexe A.4).
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(RepoLayout.FixturesDirectory, name));

    // AC-FR4-1: a Ligne that covers the Position of every Champ yields no LineTooShort, even when the
    // last declared Champ (Filler / Reserve) is truncated at the end.
    [Fact]
    [Trait("AC", "FR4-1")]
    public void Check_HeaderCoversEveryChampWithTruncatedTrailingFiller_YieldsNoError_AcFr4_1()
    {
        // Header Ligne of 19 characters: Recepteur @15 is covered, trailing Filler @18 is only
        // partially present.
        string[] lines = [Line("000000000000000000", 19), Line("000000000EOF", 25), Line("000000000999", 20)];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        Assert.Empty(errors);
    }

    // AC-FR4-1: the last declared Champ of a Bloc may be entirely absent at the end of the Ligne.
    [Fact]
    [Trait("AC", "FR4-1")]
    public void Check_LastChampEntirelyAbsentAtEndOfLigne_YieldsNoError_AcFr4_1()
    {
        // Header Ligne of exactly 18 characters: Recepteur @15 is covered, Filler @18 is missing.
        // Detail Ligne of 25 characters: OF @22 is covered, Reserve @510 is missing.
        string[] lines = [Line("000000000000000", 18), Line("000000000EOF", 25), Line("000000000999", 20)];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        Assert.Empty(errors);
    }

    // AC-FR4-1: the exemption follows the Descripteur declaration order, so the Champ declared last
    // raises no LineTooShort however far down the Ligne its Position sits, as long as every earlier
    // Champ is covered.
    [Fact]
    [Trait("AC", "FR4-1")]
    public void Check_LastDeclaredChampFarBeyondLigneEnd_IsExempt_AcFr4_1()
    {
        // Detail Ligne of 15 characters: Alpha @0 and Beta @10 are covered, Trailer @200 is not.
        string[] lines = [Line(string.Empty, 15)];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Detail }, Root(DetailWithFarTrailingChamp));

        Assert.Empty(errors);
    }

    // AC-FR4-2, AC-FR4-6: a Ligne too short to reach a Champ Position yields exactly one LineTooShort
    // carrying the Bloc, the 1-based LineNumber and the LineTooShort code.
    [Fact]
    [Trait("AC", "FR4-2")]
    public void Check_HeaderTooShortForAChamp_YieldsSingleLineTooShort_AcFr4_2()
    {
        // Header Ligne of 10 characters: Emet @12 is the first Champ whose Position is not covered.
        string[] lines = [Line("000000000", 10), Line("000000000EOF", 25), Line("000000000999", 20)];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        ConversionError error = Assert.Single(errors);
        Assert.Equal(Block.Header, error.Block);
        Assert.Equal(1, error.LineNumber);
        Assert.Equal(ErrorCode.LineTooShort, error.Code);
        Assert.Equal("Emet", error.FieldId);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    // AC-FR4-2: the Message cites the missing Position against the real Ligne length.
    [Fact]
    [Trait("AC", "FR4-2")]
    public void Check_LineTooShort_MessageCitesMissingPositionAndRealLength_AcFr4_2()
    {
        // Detail Ligne of 15 characters: Type @21 is missing.
        string[] lines = [Line("000000000000000000", 19), Line("000000000EOF", 15), Line("000000000999", 20)];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        ConversionError error = Assert.Single(errors);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal("Type", error.FieldId);
        Assert.Contains("21", error.Message, StringComparison.Ordinal);
        Assert.Contains("15", error.Message, StringComparison.Ordinal);
    }

    // AC-FR4-3: a real 19-character Entête whose last Champ Filler is declared at Position 18 is valid.
    [Theory]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(30)]
    [Trait("AC", "FR4-3")]
    public void Check_HeaderReachingLastChampPosition_IsValid_AcFr4_3(int headerLength)
    {
        string[] lines =
        [
            Line("000000000000000000", headerLength),
            Line("000000000EOF", 25),
            Line("000000000999", 20),
        ];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        Assert.Empty(errors);
    }

    // AC-FR4-4: a Pied whose length does not exceed the Records Position (12) yields LineTooShort;
    // a length past it is valid.
    [Theory]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(17, false)]
    [Trait("AC", "FR4-4")]
    public void Check_FooterLengthAgainstRecordsPosition_AcFr4_4(int footerLength, bool expectError)
    {
        string[] lines =
        [
            Line("000000000000000000", 19),
            Line("000000000EOF", 25),
            Line("000000000999", footerLength),
        ];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        if (expectError)
        {
            ConversionError error = Assert.Single(errors);
            Assert.Equal(Block.Footer, error.Block);
            Assert.Equal(ErrorCode.LineTooShort, error.Code);
            Assert.Equal(3, error.LineNumber);
        }
        else
        {
            Assert.Empty(errors);
        }
    }

    // AC-FR4-5: a Detail Ligne longer than the last declared Champ (637 > 526) raises no error; the
    // surplus is ignored (D5).
    [Fact]
    [Trait("AC", "FR4-5")]
    public void Check_DetailLongerThanLastDeclaredChamp_YieldsNoError_AcFr4_5()
    {
        string[] lines =
        [
            Line("000000000000000000", 19),
            Line("000000000EOF", 637),
            Line("000000000999", 20),
        ];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Header, Block.Detail, Block.Footer }, Root(Kape22Profile));

        Assert.Empty(errors);
    }

    // AC-FR4-6: a Ligne too short for several Champs still yields a single LineTooShort, citing the
    // first missing Champ Position.
    [Fact]
    [Trait("AC", "FR4-6")]
    public void Check_LigneTooShortForSeveralChamps_YieldsSingleErrorForFirstMissing_AcFr4_6()
    {
        // Detail Ligne of 2 characters: Beta @10 and Gamma @20 are both missing, Alpha @0 is present.
        string[] lines = [Line(string.Empty, 2)];

        var errors = LineLengthChecker.Check(lines, new[] { Block.Detail }, Root(MessageOnlyProfile));

        ConversionError error = Assert.Single(errors);
        Assert.Equal(ErrorCode.LineTooShort, error.Code);
        Assert.Contains("10", error.Message, StringComparison.Ordinal);
    }

    // AC-FR4-6: within a multi-Ligne Detail Bloc, every too-short Ligne contributes its own single
    // Error, in Ligne order, and Lignes that are long enough contribute none.
    [Fact]
    [Trait("AC", "FR4-6")]
    public void Check_MultiLigneDetailBloc_OneErrorPerTooShortLigne_AcFr4_6()
    {
        string[] lines =
        [
            Line(string.Empty, 3),
            Line(string.Empty, 25),
            Line(string.Empty, 4),
        ];

        var errors = LineLengthChecker.Check(
            lines, new[] { Block.Detail, Block.Detail, Block.Detail }, Root(MessageOnlyProfile));

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal(ErrorCode.LineTooShort, e.Code));
        Assert.Equal(new[] { 1, 3 }, errors.Select(e => e.LineNumber).ToArray());
    }

    // AC-FR4-2: the failure surfaces through Converter.Convert as the single blocking error, no Xml.
    [Fact]
    [Trait("AC", "FR4-2")]
    public void Convert_DetailLigneTooShort_SurfacesLineTooShort_AcFr4_2()
    {
        string fichier = string.Join(
            "\r\n",
            Line("000000000000000000", 25),
            Line("000000000EOF", 15),
            Line("000000000999", 20));

        ConversionResult result = Converter.Convert(Windows1252(fichier), Kape22Profile);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.LineTooShort, error.Code);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal(2, error.LineNumber);
        Assert.Null(result.Xml);
    }

    // AC-FR4-1: a Fichier whose Lignes all cover their Champ Positions produces no LineTooShort.
    [Fact]
    [Trait("AC", "FR4-1")]
    public void Convert_AllLignesCoverTheirChamps_YieldsNoLineTooShort_AcFr4_1()
    {
        string fichier = string.Join(
            "\r\n",
            Line("000000000000000000", 25),
            Line("000000000EOF", 30),
            Line("000000000999", 20));

        ConversionResult result = Converter.Convert(Windows1252(fichier), Kape22Profile);

        Assert.DoesNotContain(result.Errors, e => e.Code == ErrorCode.LineTooShort);
    }

    // AC-FR4-5: an overlong Detail Ligne surfaces no error through Converter.Convert either.
    [Fact]
    [Trait("AC", "FR4-5")]
    public void Convert_OverlongDetailLigne_YieldsNoError_AcFr4_5()
    {
        string fichier = string.Join(
            "\r\n",
            Line("000000000000000000", 25),
            Line("000000000EOF", 637),
            Line("000000000999", 20));

        ConversionResult result = Converter.Convert(Windows1252(fichier), Kape22Profile);

        Assert.DoesNotContain(result.Errors, e => e.Code == ErrorCode.LineTooShort);
    }

    // AC-FR4-2, AC-FR4-6: the faulty fixture detail_too_short.txt (Annexe A.4) yields a single
    // Detail-level LineTooShort on Ligne 2.
    [Fact]
    [Trait("AC", "FR4-2")]
    public void Convert_DetailTooShortFixture_YieldsSingleDetailLineTooShort_AcFr4_2()
    {
        ConversionResult result = Converter.Convert(Fixture("detail_too_short.txt"), Kape22FixtureProfile);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.LineTooShort, error.Code);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal(2, error.LineNumber);
    }
}
