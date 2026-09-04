using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static TextToXml.Tests.TestSupport;

namespace TextToXml.Tests;

// Story 1.4 - Bloc assignment, Ligne-count check and non-blocking Segment control (FR-3).
// TDD: these tests are written before BlockAssigner's logic and its wiring into Converter (CC-1).
// Vocabulary follows the PRD glossary section 3 (Fichier, Ligne, Bloc, Segment, Warning...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class BlockAssignmentTests
{
    // KAPE22 profile: <header> + <footer>, exactly one message expected, so exactly 3 non-empty Lignes.
    // No Segment control here, so role assignment can be checked in isolation.
    private const string Kape22HeaderMessageFooter = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1">
          <header>
            <value Id="Segment" Position="0" Size="3" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="Segment" Position="0" Size="3" />
          </message>
          <footer>
            <value Id="Segment" Position="0" Size="3" />
          </footer>
        </commande>
        """;

    // Same KAPE22 profile plus the Segment control: expected marker is "000" on the Header,
    // "EOF" on the Detail and "999" on the Footer.
    private const string Kape22WithSegmentControl = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="EOF" footerMarker="999">
          <header>
            <value Id="Segment" Position="0" Size="3" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="Segment" Position="0" Size="3" />
          </message>
          <footer>
            <value Id="Segment" Position="0" Size="3" />
          </footer>
        </commande>
        """;

    // KAPE22-profile descriptor matching the faulty fixtures under fixtures/ (Annexe A.4): File @0,
    // NumeroFichier @6, Segment @9, all Size 3; Segment markers "000" / "EOF" / "999" (D16).
    private const string Kape22FixtureProfile = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="EOF" footerMarker="999">
          <header>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="NumeroFichier" Position="6" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
          </message>
          <footer>
            <value Id="File" Position="0" Size="3" datatype="string" />
            <value Id="Segment" Position="9" Size="3" datatype="string" />
            <value Id="Records" Position="12" Size="5" datatype="int" />
          </footer>
        </commande>
        """;

    // No <header>, no <footer>, no expectedMessageCount: every Ligne becomes a Detail.
    private const string MessageOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only descriptor plus the Segment control, so a Detail Bloc spanning several Lignes can
    // be exercised (one distinct Warning per deviating Ligne).
    private const string MessageOnlyWithSegmentControl = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed" segmentField="Segment" messageMarker="EOF">
          <message type="GEN" index="0">
            <value Id="Segment" Position="0" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // <header> only, no <footer>, no expectedMessageCount: first Ligne is the Header, the rest are Details.
    private const string HeaderOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <header>
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </header>
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // <header> + <footer> but no expectedMessageCount: at least one Detail Ligne is required.
    private const string HeaderFooterNoExpectedCount = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <header>
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </header>
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </message>
          <footer>
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </footer>
        </commande>
        """;

    // Message-only descriptor that still pins the message count to one.
    private const string MessageOnlyExpectingOne = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed" expectedMessageCount="1">
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    private static ConversionResult Convert(string descriptor, string fichier) =>
        Converter.Convert(Windows1252(fichier), descriptor);

    // Reads one of the faulty fixture files that live directly under fixtures/ (Annexe A.4).
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(RepoLayout.FixturesDirectory, name));

    // AC-FR3-1: KAPE22 profile, a Fichier that is not exactly 3 non-empty Lignes yields a single
    // File-level WrongBlockCount and no Bloc is assigned (no Champ analysis).
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(1)]
    [Trait("AC", "FR3-1")]
    public void Assign_Kape22Profile_WrongLigneCount_YieldsSingleWrongBlockCount_AcFr3_1(int ligneCount)
    {
        string[] lignes = Enumerable.Repeat("SEG", ligneCount).ToArray();

        BlockAssignmentResult result = BlockAssigner.Assign(lignes, Root(Kape22HeaderMessageFooter));

        Assert.NotNull(result.Error);
        Assert.Equal(Block.File, result.Error!.Block);
        Assert.Equal(0, result.Error.LineNumber);
        Assert.Equal(ErrorCode.WrongBlockCount, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Empty(result.Blocks);
    }

    // AC-FR3-1: the WrongBlockCount message cites the expected count against the found count.
    [Fact]
    [Trait("AC", "FR3-1")]
    public void Assign_Kape22Profile_WrongLigneCount_MessageCitesExpectedVsFound_AcFr3_1()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(["SEG", "SEG"], Root(Kape22HeaderMessageFooter));

        Assert.NotNull(result.Error);
        Assert.Contains("3", result.Error!.Message, StringComparison.Ordinal);
        Assert.Contains("2", result.Error.Message, StringComparison.Ordinal);
    }

    // AC-FR3-1: with <header> and <footer> but no expectedMessageCount, too few Lignes still yields
    // WrongBlockCount and the fallback message cites the minimum expected against the found count.
    [Fact]
    [Trait("AC", "FR3-1")]
    public void Assign_HeaderFooterNoExpectedCount_TooFewLignes_YieldsWrongBlockCountCitingMinimum_AcFr3_1()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(["SEG", "SEG"], Root(HeaderFooterNoExpectedCount));

        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.WrongBlockCount, result.Error!.Code);
        Assert.Contains("3", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("2", result.Error.Message, StringComparison.Ordinal);
    }

    // AC-FR3-1: a Fichier with no non-empty Ligne is reported as WrongBlockCount without throwing.
    [Fact]
    [Trait("AC", "FR3-1")]
    public void Assign_NoNonEmptyLigne_YieldsWrongBlockCountWithoutThrowing_AcFr3_1()
    {
        BlockAssignmentResult? result = null;
        Exception? exception = Record.Exception(
            () => result = BlockAssigner.Assign(["", "   "], Root(Kape22HeaderMessageFooter)));

        Assert.Null(exception);
        Assert.NotNull(result!.Error);
        Assert.Equal(ErrorCode.WrongBlockCount, result.Error!.Code);
    }

    // AC-FR3-1: surfaced through Converter.Convert as the single blocking error.
    [Fact]
    [Trait("AC", "FR3-1")]
    public void Convert_Kape22Profile_WrongLigneCount_SurfacesWrongBlockCount_AcFr3_1()
    {
        ConversionResult result = Convert(Kape22HeaderMessageFooter, "SEG\r\nSEG\r\nSEG\r\nSEG");

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.WrongBlockCount, error.Code);
    }

    // AC-FR3-1: the faulty fixtures two_lines.txt / four_lines.txt (Annexe A.4) yield WrongBlockCount
    // against the KAPE22 profile.
    [Theory]
    [InlineData("two_lines.txt")]
    [InlineData("four_lines.txt")]
    [Trait("AC", "FR3-1")]
    public void Convert_Kape22FixtureProfile_WrongLigneCountFixture_YieldsWrongBlockCount_AcFr3_1(string fixture)
    {
        ConversionResult result = Converter.Convert(Fixture(fixture), Kape22FixtureProfile);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.WrongBlockCount, error.Code);
    }

    // AC-FR3-2: KAPE22 profile with 3 Lignes assigns Header, Detail, Footer in Ligne order.
    [Fact]
    [Trait("AC", "FR3-2")]
    public void Assign_Kape22Profile_ThreeLignes_AssignsHeaderDetailFooter_AcFr3_2()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(["SEG", "SEG", "SEG"], Root(Kape22HeaderMessageFooter));

        Assert.Null(result.Error);
        Assert.Equal(new[] { Block.Header, Block.Detail, Block.Footer }, result.Blocks);
    }

    // AC-FR3-3: no <header>, no <footer>, no expectedMessageCount, 5 Lignes yields 5 Detail Blocs and no error.
    [Fact]
    [Trait("AC", "FR3-3")]
    public void Assign_NoHeaderNoFooterNoExpectedCount_FiveLignes_AllDetail_AcFr3_3()
    {
        string[] lignes = Enumerable.Repeat("ABC", 5).ToArray();

        BlockAssignmentResult result = BlockAssigner.Assign(lignes, Root(MessageOnly));

        Assert.Null(result.Error);
        Assert.Equal(Enumerable.Repeat(Block.Detail, 5).ToArray(), result.Blocks);
    }

    // AC-FR3-4: <header> only, 4 Lignes yields Header then three Details.
    [Fact]
    [Trait("AC", "FR3-4")]
    public void Assign_HeaderOnly_FourLignes_HeaderThenThreeDetails_AcFr3_4()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(["ABC", "ABC", "ABC", "ABC"], Root(HeaderOnly));

        Assert.Null(result.Error);
        Assert.Equal(new[] { Block.Header, Block.Detail, Block.Detail, Block.Detail }, result.Blocks);
    }

    // AC-FR3-5: expectedMessageCount="1" but 2 Detail Lignes yields WrongBlockCount.
    [Fact]
    [Trait("AC", "FR3-5")]
    public void Assign_ExpectedMessageCountOne_TwoDetailLignes_YieldsWrongBlockCount_AcFr3_5()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(["ABC", "ABC"], Root(MessageOnlyExpectingOne));

        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.WrongBlockCount, result.Error!.Code);
        Assert.Empty(result.Blocks);
    }

    // AC-FR3-5: the same mismatch surfaces through Converter.Convert.
    [Fact]
    [Trait("AC", "FR3-5")]
    public void Convert_ExpectedMessageCountOne_TwoDetailLignes_SurfacesWrongBlockCount_AcFr3_5()
    {
        ConversionResult result = Convert(MessageOnlyExpectingOne, "ABC\r\nABC");

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.WrongBlockCount, error.Code);
    }

    // AC-FR3-6: a Bloc whose Segment Champ differs from its expected marker yields a SegmentMismatch
    // Warning; the Error stays null and the Blocs are still assigned.
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Assign_DetailSegmentDiffersFromMarker_YieldsSegmentMismatchWarning_AcFr3_6()
    {
        // Header reads "000" (expected "000"), Footer reads "999" (expected "999"),
        // Detail reads "000" but the messageMarker is "EOF".
        BlockAssignmentResult result = BlockAssigner.Assign(["000", "000", "999"], Root(Kape22WithSegmentControl));

        Assert.Null(result.Error);
        ConversionError warning = Assert.Single(result.Warnings);
        Assert.Equal(Block.Detail, warning.Block);
        Assert.Equal(2, warning.LineNumber);
        Assert.Equal("Segment", warning.FieldId);
        Assert.Equal(ErrorCode.SegmentMismatch, warning.Code);
        Assert.Equal("000", warning.RawValue);
        Assert.False(string.IsNullOrWhiteSpace(warning.Message));
    }

    // AC-FR3-6: a SegmentMismatch alone leaves Success unchanged and the Fichier is still processed.
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Convert_SegmentMismatchAlone_KeepsSuccessAndProcessesTheFichier_AcFr3_6()
    {
        ConversionResult result = Convert(Kape22WithSegmentControl, "000\r\n000\r\n999");

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Warnings, w => w.Code == ErrorCode.SegmentMismatch);
    }

    // AC-FR3-6: each Segment deviation is a distinct Warning, across different sections.
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Assign_SeveralSegmentMismatches_EachIsADistinctWarning_AcFr3_6()
    {
        // Header reads "111" (expected "000") and Detail reads "222" (expected "EOF"); Footer is fine.
        BlockAssignmentResult result = BlockAssigner.Assign(["111", "222", "999"], Root(Kape22WithSegmentControl));

        Assert.Null(result.Error);
        Assert.Equal(2, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Equal(ErrorCode.SegmentMismatch, w.Code));
        Assert.Equal(new[] { 1, 2 }, result.Warnings.Select(w => w.LineNumber).ToArray());
    }

    // AC-FR3-6: within a single multi-Ligne Detail Bloc, every deviating Ligne is its own Warning.
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Assign_MultiLigneDetailBloc_OneWarningPerDeviatingLigne_AcFr3_6()
    {
        // Five Detail Lignes; Lignes 1, 3 and 5 deviate from the "EOF" messageMarker.
        BlockAssignmentResult result = BlockAssigner.Assign(
            ["000", "EOF", "111", "EOF", "222"], Root(MessageOnlyWithSegmentControl));

        Assert.Null(result.Error);
        Assert.Equal(3, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Equal(ErrorCode.SegmentMismatch, w.Code));
        Assert.Equal(new[] { 1, 3, 5 }, result.Warnings.Select(w => w.LineNumber).ToArray());
    }

    // AC-FR3-6: the faulty fixture segment_mismatch.txt (Annexe A.4) warns on Ligne 2 and keeps Success.
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Convert_SegmentMismatchFixture_WarnsOnDetailLigneAndKeepsSuccess_AcFr3_6()
    {
        ConversionResult result = Converter.Convert(Fixture("segment_mismatch.txt"), Kape22FixtureProfile);

        Assert.True(result.Success);
        ConversionError warning = Assert.Single(result.Warnings);
        Assert.Equal(ErrorCode.SegmentMismatch, warning.Code);
        Assert.Equal(Block.Detail, warning.Block);
        Assert.Equal(2, warning.LineNumber);
        Assert.Equal("000", warning.RawValue);
    }

    // AC-FR3-6: a valid KAPE22 Fichier whose Segment values match every marker converts with no
    // Error and no Warning (the control raises nothing when it should not).
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Convert_AllSegmentMarkersMatch_SucceedsWithNoErrorAndNoWarning_AcFr3_6()
    {
        ConversionResult result = Convert(Kape22WithSegmentControl, "000\r\nEOF\r\n999");

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    // AC-FR3-6: a Ligne too short to carry the whole Segment Champ raises no Warning and no exception
    // (Story 1.5 reports the truncation separately).
    [Fact]
    [Trait("AC", "FR3-6")]
    public void Assign_LigneTooShortForSegmentChamp_RaisesNoWarning_AcFr3_6()
    {
        // Segment is declared at Position 9 Size 3; the Detail Ligne stops at 10 characters, so the
        // control cannot read it. Header and Footer carry matching markers.
        BlockAssignmentResult? result = null;
        Exception? exception = Record.Exception(
            () => result = BlockAssigner.Assign(
                ["P60000001000", "P60000001A", "P60000001999"], Root(Kape22FixtureProfile)));

        Assert.Null(exception);
        Assert.Null(result!.Error);
        Assert.Empty(result.Warnings);
    }

    // AC-FR3-7: trailing empty or whitespace-only Lignes are dropped before the count.
    [Fact]
    [Trait("AC", "FR3-7")]
    public void Assign_TrailingEmptyLignes_AreIgnoredBeforeTheCount_AcFr3_7()
    {
        BlockAssignmentResult result = BlockAssigner.Assign(
            ["SEG", "SEG", "SEG", "", "   ", ""], Root(Kape22HeaderMessageFooter));

        Assert.Null(result.Error);
        Assert.Equal(new[] { Block.Header, Block.Detail, Block.Footer }, result.Blocks);
    }

    // AC-FR3-7: an empty or whitespace-only Ligne in the middle counts as a Ligne, so the count check fails.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("AC", "FR3-7")]
    public void Assign_EmptyOrWhitespaceLigneInTheMiddle_CountsAsALigne_AcFr3_7(string middle)
    {
        BlockAssignmentResult result = BlockAssigner.Assign(
            ["SEG", middle, "SEG", "SEG"], Root(Kape22HeaderMessageFooter));

        Assert.NotNull(result.Error);
        Assert.Equal(ErrorCode.WrongBlockCount, result.Error!.Code);
    }

    // AC-FR3-7: a trailing LF does not add a Ligne, so the KAPE22 count check still passes.
    [Fact]
    [Trait("AC", "FR3-7")]
    public void Convert_TrailingEmptyLignes_DoNotTriggerWrongBlockCount_AcFr3_7()
    {
        ConversionResult result = Convert(Kape22HeaderMessageFooter, "SEG\r\nSEG\r\nSEG\r\n\r\n");

        Assert.DoesNotContain(result.Errors, e => e.Code == ErrorCode.WrongBlockCount);
    }

    // AC-FR3-8: WrongBlockCount (Error) short-circuits Champ analysis, so no Segment Warning is raised
    // even though the Detail Ligne would mismatch its marker.
    [Fact]
    [Trait("AC", "FR3-8")]
    public void Convert_WrongLigneCountWithSegmentMismatch_ErrorOnlyNoWarning_AcFr3_8()
    {
        ConversionResult result = Convert(Kape22WithSegmentControl, "000\r\n000\r\n999\r\n000");

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.WrongBlockCount, error.Code);
        Assert.Empty(result.Warnings);
    }

    // AC-FR3-8: with the correct Ligne count, a SegmentMismatch Warning coexists with Success == true.
    [Fact]
    [Trait("AC", "FR3-8")]
    public void Convert_CorrectCountWithSegmentMismatch_WarningCoexistsWithSuccess_AcFr3_8()
    {
        ConversionResult result = Convert(Kape22WithSegmentControl, "000\r\n000\r\n999");

        Assert.True(result.Success);
        Assert.Single(result.Warnings, w => w.Code == ErrorCode.SegmentMismatch);
    }
}
