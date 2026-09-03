using System;
using System.Linq;

namespace TextToXml.Tests;

// Story 1.3 - Strict Windows-1252 decoding and splitting into Lignes (FR-2).
// TDD: these tests are written before InputReader's decoding and splitting logic (CC-1).
// Vocabulary follows the PRD glossary section 3 (Fichier, Ligne, Bloc...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class InputDecodingTests
{
    // Minimal well-formed message-only Descripteur, so Converter.Convert gets past validation
    // and reaches the decoding stage.
    private const string ValidMessageOnly = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Code" Position="0" Size="4" datatype="string" />
          </message>
        </commande>
        """;

    // The five byte values that have no assigned character in Windows-1252; a strict decoder
    // must reject each of them rather than substitute or throw out of Read (D19).
    public static TheoryData<byte> UndecodableWindows1252Bytes() =>
        new() { 0x81, 0x8D, 0x8F, 0x90, 0x9D };

    private static ConversionResult Convert(byte[] input) => Converter.Convert(input, ValidMessageOnly);

    // The decoding stage reports its two failures the same way the layout check does:
    // a single File-level error at LineNumber 0, and no exception.
    private static void AssertSingleFileError(InputReadResult result, ErrorCode expected)
    {
        Assert.NotNull(result.Error);
        Assert.Equal(Block.File, result.Error!.Block);
        Assert.Equal(0, result.Error.LineNumber);
        Assert.Equal(expected, result.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Message));
        Assert.Empty(result.Lines);
    }

    [Fact]
    [Trait("AC", "FR2-1")]
    public void Read_ZeroByteFichier_YieldsEmptyFileError_AcFr2_1()
    {
        InputReadResult result = InputReader.Read(ReadOnlySpan<byte>.Empty);

        AssertSingleFileError(result, ErrorCode.EmptyFile);
    }

    [Fact]
    [Trait("AC", "FR2-1")]
    public void Convert_ZeroByteFichier_SurfacesSingleEmptyFileError_AcFr2_1()
    {
        ConversionResult result = Convert(Array.Empty<byte>());

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(0, error.LineNumber);
        Assert.Equal(ErrorCode.EmptyFile, error.Code);
    }

    public static TheoryData<string> WhitespaceOnlyFichiers() =>
        new()
        {
            "   ",
            "\r\n\r\n",
            "\n \n\t\n",
            "        \r\n     ",
        };

    [Theory]
    [MemberData(nameof(WhitespaceOnlyFichiers))]
    [Trait("AC", "FR2-2")]
    public void Read_FichierOfSpacesAndLineBreaksOnly_YieldsEmptyFileError_AcFr2_2(string content)
    {
        InputReadResult result = InputReader.Read(Windows1252(content));

        AssertSingleFileError(result, ErrorCode.EmptyFile);
    }

    [Theory]
    [MemberData(nameof(WhitespaceOnlyFichiers))]
    [Trait("AC", "FR2-2")]
    public void Convert_FichierOfSpacesAndLineBreaksOnly_SurfacesEmptyFileError_AcFr2_2(string content)
    {
        ConversionResult result = Convert(Windows1252(content));

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.EmptyFile, error.Code);
    }

    [Fact]
    [Trait("AC", "FR2-3")]
    public void Read_ByteE9InATextChamp_DecodesToEAcute_AcFr2_3()
    {
        // 0xE9 is "é" in Windows-1252. The library registers CodePagesEncodingProvider itself, so
        // decoding must succeed with no external setup (AR-10): the decoded Ligne must contain "é",
        // never "?" and never an exception.
        byte[] input = [0x41, 0xE9, 0x42];

        InputReadResult result = InputReader.Read(input);

        Assert.Null(result.Error);
        string ligne = Assert.Single(result.Lines);
        Assert.Equal("AéB", ligne);
    }

    [Theory]
    [MemberData(nameof(UndecodableWindows1252Bytes))]
    [Trait("AC", "FR2-4")]
    public void Read_ByteUndecodableInWindows1252_YieldsUndecodableInputError_AcFr2_4(byte undecodable)
    {
        byte[] input = [0x41, undecodable, 0x42];

        InputReadResult? result = null;
        Exception? exception = Record.Exception(() => result = InputReader.Read(input));

        Assert.Null(exception);
        AssertSingleFileError(result!, ErrorCode.UndecodableInput);
    }

    [Fact]
    [Trait("AC", "FR2-4")]
    public void Convert_ByteUndecodableInWindows1252_SurfacesUndecodableInputError_AcFr2_4()
    {
        ConversionResult result = Convert([0x41, 0x81, 0x42]);

        Assert.False(result.Success);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(0, error.LineNumber);
        Assert.Equal(ErrorCode.UndecodableInput, error.Code);
    }

    // A C0 control byte decodes cleanly in Windows-1252 but is not a legal XML 1.0 character, so it is
    // rejected at decode time as UndecodableInput rather than throwing later while the XML is written.
    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x07)]
    [InlineData((byte)0x0B)]
    [InlineData((byte)0x0C)]
    [InlineData((byte)0x1F)]
    [Trait("AC", "FR2-4")]
    public void Read_C0ControlByte_YieldsUndecodableInputError_AcFr2_4(byte control)
    {
        byte[] input = [0x41, control, 0x42];

        InputReadResult? result = null;
        Exception? exception = Record.Exception(() => result = InputReader.Read(input));

        Assert.Null(exception);
        AssertSingleFileError(result!, ErrorCode.UndecodableInput);
    }

    // Tab, line feed and carriage return are the only control characters XML 1.0 allows, so they must
    // pass the decode stage untouched.
    [Fact]
    [Trait("AC", "FR2-4")]
    public void Read_TabLineFeedAndCarriageReturn_AreNotRejected_AcFr2_4()
    {
        InputReadResult result = InputReader.Read(Windows1252("A\tB\r\nC\tD"));

        Assert.Null(result.Error);
        Assert.Equal(new[] { "A\tB", "C\tD" }, result.Lines);
    }

    [Fact]
    [Trait("AC", "FR2-5")]
    public void Read_FichierWithoutTrailingLf_KeepsTheLastLigne_AcFr2_5()
    {
        InputReadResult result = InputReader.Read(Windows1252("AAAA\nBBBB\nCCCC"));

        Assert.Null(result.Error);
        Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, result.Lines);
    }

    [Fact]
    [Trait("AC", "FR2-6")]
    public void Read_MixedLfAndCrLfEndings_SplitsCorrectlyAndStripsResidualCr_AcFr2_6()
    {
        InputReadResult result = InputReader.Read(Windows1252("AAAA\r\nBBBB\nCCCC\r\n"));

        Assert.Null(result.Error);
        Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, result.Lines);
        Assert.DoesNotContain(result.Lines, ligne => ligne.Contains('\r'));
    }

    [Fact]
    [Trait("AC", "FR2-7")]
    public void Read_TrailingLf_DoesNotAddAnEmptyLigne_AcFr2_7()
    {
        InputReadResult withTrailingLf = InputReader.Read(Windows1252("AAAA\nBBBB\n"));
        InputReadResult withoutTrailingLf = InputReader.Read(Windows1252("AAAA\nBBBB"));

        Assert.Equal(withoutTrailingLf.Lines, withTrailingLf.Lines);
        Assert.Equal(2, withTrailingLf.Lines.Count);
    }

    // Encodes test text to Windows-1252 bytes. Only characters in the ASCII range are used by the
    // callers, so a plain byte cast is enough and needs no encoding provider on the test side.
    private static byte[] Windows1252(string text) => text.Select(c => (byte)c).ToArray();
}
