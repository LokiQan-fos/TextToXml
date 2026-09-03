using System;
using System.Linq;

namespace TextToXml.Tests;

// Story 1.2 - Descriptor loading & validation (FR-1).
// TDD: these tests are written before Converter's descriptor-validation logic (CC-1).
// Vocabulary follows the PRD glossary section 3 (Descripteur, Champ, Bloc, ConversionError...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class DescriptorValidationTests
{
    // A well-formed, valid Fixed descriptor with header, message and footer. Overlapping slices on purpose
    // (Segment and NumeroFichier both at Position 9) to match the corrected P60.xml (D23).
    private const string ValidHeaderMessageFooter = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="KAPE22" format="Fixed" expectedMessageCount="1" segmentField="Segment" headerMarker="000" messageMarker="000" footerMarker="999">
          <header>
            <value Id="File" Position="0" Size="3" />
            <value Id="NumeroFichier" Position="6" Size="3" />
            <value Id="Segment" Position="9" Size="3" />
          </header>
          <message type="KAPE22" index="0">
            <value Id="File" Position="0" Size="3" />
            <value Id="NumeroFichier" Position="9" Size="3" />
            <value Id="Segment" Position="9" Size="3" />
            <value Id="OF" Position="22" Size="7" datatype="string" />
          </message>
          <footer>
            <value Id="File" Position="0" Size="3" />
            <value Id="Segment" Position="9" Size="3" />
            <value Id="Records" Position="12" Size="5" datatype="int" />
          </footer>
        </commande>
        """;

    // Message-only descriptor: no header, no footer, no Segment control attributes.
    private const string ValidMessageOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="4" datatype="string" />
            <value Id="Quantite" Position="4" Size="6" datatype="int" />
          </message>
        </commande>
        """;

    private static ConversionResult Convert(string descriptor, byte[]? input = null)
    {
        return Converter.Convert(input ?? Array.Empty<byte>(), descriptor);
    }

    // The descriptor is loaded before decoding, so an invalid layout is reported without touching the input.
    private static ConversionError AssertSingleLayoutInvalid(ConversionResult result)
    {
        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(0, error.LineNumber);
        Assert.Equal(ErrorCode.LayoutInvalid, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        return error;
    }

    private static bool HasLayoutInvalid(ConversionResult result)
    {
        return result.Errors.Any(e => e.Code == ErrorCode.LayoutInvalid);
    }

    [Fact]
    [Trait("AC", "FR1-1")]
    public void Convert_NotWellFormedDescriptorXml_YieldsSingleLayoutInvalid_AcFr1_1()
    {
        string broken = "<commande type=\"KAPE22\" format=\"Fixed\"><message><value Id=\"A\" Position=\"0\" Size=\"1\" ></commande>";

        AssertSingleLayoutInvalid(Convert(broken));
    }

    [Fact]
    [Trait("AC", "FR1-2")]
    public void Convert_DescriptorWithoutMessageSection_YieldsLayoutInvalid_AcFr1_2()
    {
        string headerOnly = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <header>
                <value Id="File" Position="0" Size="3" />
              </header>
            </commande>
            """;

        AssertSingleLayoutInvalid(Convert(headerOnly));
    }

    [Fact]
    [Trait("AC", "FR1-3")]
    public void Convert_DuplicateValueIdInSameBloc_YieldsLayoutInvalid_AcFr1_3()
    {
        string duplicateId = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <message type="KAPE22" index="0">
                <value Id="OF" Position="0" Size="7" datatype="string" />
                <value Id="OF" Position="7" Size="7" datatype="string" />
              </message>
            </commande>
            """;

        ConversionError error = AssertSingleLayoutInvalid(Convert(duplicateId));
        Assert.Contains("OF", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1Diametre")]
    [InlineData("Diametre produit")]
    [InlineData("ns:Diametre")]
    [Trait("AC", "FR1-4")]
    public void Convert_ChampIdNotAValidXmlName_YieldsLayoutInvalidCitingTheId_AcFr1_4(string id)
    {
        // The Id becomes an element name in the normalized XML, so an illegal name must be caught here
        // rather than throwing while the XML is built.
        string descriptor = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <message type="KAPE22" index="0">
                <value Id="{id}" Position="0" Size="4" datatype="string" />
              </message>
            </commande>
            """;

        ConversionError error = AssertSingleLayoutInvalid(Convert(descriptor));
        Assert.Contains(id, error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidPositionOrSizeCases()
    {
        return new TheoryData<string, string>
        {
            { "missing Position", "<value Id=\"Diametre\" Size=\"4\" datatype=\"int\" />" },
            { "missing Size", "<value Id=\"Diametre\" Position=\"4\" datatype=\"int\" />" },
            { "negative Position", "<value Id=\"Diametre\" Position=\"-4\" Size=\"4\" datatype=\"int\" />" },
            { "negative Size", "<value Id=\"Diametre\" Position=\"4\" Size=\"-1\" datatype=\"int\" />" },
            { "non-integer Position", "<value Id=\"Diametre\" Position=\"abc\" Size=\"4\" datatype=\"int\" />" },
            { "non-integer Size", "<value Id=\"Diametre\" Position=\"4\" Size=\"4.5\" datatype=\"int\" />" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidPositionOrSizeCases))]
    [Trait("AC", "FR1-4")]
    public void Convert_InvalidPositionOrSize_YieldsLayoutInvalidCitingTheId_AcFr1_4(string caseName, string valueElement)
    {
        _ = caseName;
        string descriptor = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <message type="KAPE22" index="0">
                {valueElement}
              </message>
            </commande>
            """;

        ConversionError error = AssertSingleLayoutInvalid(Convert(descriptor));
        Assert.Contains("Diametre", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("timestamp")]
    [InlineData("number")]
    [InlineData("bool")]
    [InlineData("")]
    [Trait("AC", "FR1-5")]
    public void Convert_UnknownDatatype_YieldsLayoutInvalid_AcFr1_5(string datatype)
    {
        string descriptor = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <message type="KAPE22" index="0">
                <value Id="Champ" Position="0" Size="4" datatype="{datatype}" />
              </message>
            </commande>
            """;

        AssertSingleLayoutInvalid(Convert(descriptor));
    }

    [Fact]
    [Trait("AC", "FR1-6")]
    public void Convert_ValidDescriptorWithoutHeaderAndFooter_IsAccepted_AcFr1_6()
    {
        ConversionResult result = Convert(ValidMessageOnly);

        Assert.False(HasLayoutInvalid(result));
    }

    [Fact]
    [Trait("AC", "FR1-7")]
    public void Convert_OverlappingChampSlices_AreAcceptedWithoutError_AcFr1_7()
    {
        // Segment and NumeroFichier both at Position 9 in the message Bloc (D23).
        ConversionResult result = Convert(ValidHeaderMessageFooter);

        Assert.False(HasLayoutInvalid(result));
    }

    [Fact]
    [Trait("AC", "FR1-8")]
    public void Convert_NullDescriptor_ThrowsArgumentNullException_AcFr1_8()
    {
        Assert.Throws<ArgumentNullException>(() => Converter.Convert(Array.Empty<byte>(), null!));
    }

    // The story's opening Given/Then: a well-formed, valid Descriptor is accepted and conversion continues.
    [Fact]
    public void Convert_WellFormedValidDescriptor_IsAcceptedAndContinues()
    {
        ConversionResult result = Convert(ValidHeaderMessageFooter);

        Assert.False(HasLayoutInvalid(result));
    }

    [Fact]
    public void Convert_RootElementIsNotCommande_YieldsLayoutInvalid()
    {
        string wrongRoot = """
            <?xml version="1.0" encoding="utf-8"?>
            <layout format="Fixed">
              <message type="X" index="0">
                <value Id="A" Position="0" Size="3" />
              </message>
            </layout>
            """;

        AssertSingleLayoutInvalid(Convert(wrongRoot));
    }

    [Fact]
    public void Convert_DescriptorWithXmlNamespace_YieldsLayoutInvalid()
    {
        string namespaced = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande xmlns="urn:example" format="Fixed">
              <message type="X" index="0">
                <value Id="A" Position="0" Size="3" />
              </message>
            </commande>
            """;

        AssertSingleLayoutInvalid(Convert(namespaced));
    }

    [Fact]
    public void Convert_ValueWithoutId_YieldsLayoutInvalid()
    {
        string missingId = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed">
              <message type="KAPE22" index="0">
                <value Position="0" Size="3" datatype="string" />
              </message>
            </commande>
            """;

        AssertSingleLayoutInvalid(Convert(missingId));
    }

    [Fact]
    [Trait("AC", "FR1-10")]
    public void Convert_DescriptorWithoutSegmentField_ProducesNoSegmentMismatch_AcFr1_10()
    {
        // Three lines so block assignment succeeds; the point is that no Segment control runs.
        byte[] input = "AAA\r\nBBB\r\nCCC"u8.ToArray();

        ConversionResult result = Convert(ValidMessageOnly, input);

        Assert.False(HasLayoutInvalid(result));
        Assert.DoesNotContain(result.Warnings, w => w.Code == ErrorCode.SegmentMismatch);
    }

    [Fact]
    [Trait("AC", "FR1-11")]
    public void Convert_SegmentFieldPointingToUnknownId_YieldsLayoutInvalid_AcFr1_11()
    {
        string descriptor = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="KAPE22" format="Fixed" segmentField="Segment" messageMarker="000">
              <message type="KAPE22" index="0">
                <value Id="Code" Position="0" Size="4" datatype="string" />
              </message>
            </commande>
            """;

        AssertSingleLayoutInvalid(Convert(descriptor));
    }

    [Fact]
    [Trait("AC", "FR1-12")]
    public void Convert_SemicolonFormat_YieldsLayoutInvalidNotSupportedInV1_AcFr1_12()
    {
        string descriptor = """
            <?xml version="1.0" encoding="utf-8"?>
            <commande type="BIL" format="Semicolon">
              <message type="BIL" index="0">
                <value Id="OF" Position="1" Size="7" datatype="int" />
              </message>
            </commande>
            """;

        ConversionError error = AssertSingleLayoutInvalid(Convert(descriptor));
        Assert.Contains("non supporté en v1", error.Message, StringComparison.Ordinal);
    }
}
