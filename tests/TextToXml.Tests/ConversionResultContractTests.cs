using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using static TextToXml.Tests.TestSupport;

namespace TextToXml.Tests;

// Story 1.7 - ConversionResult contract, purity and thread-safety (FR-6, plus NFR-1 and NFR-3).
// This story adds no production behavior: stages 1.2 to 1.6 already satisfy the contract. These tests
// lock the FR-6 invariants (the Success / Xml correlation, ascending LineNumber ordering, clean French
// Messages, no exception on corrupt input, JSON serialization) and the NFR-3 thread-safety guarantee,
// so a later change to any stage cannot break them silently (CC-1).
// Vocabulary follows the PRD glossary section 3 (Fichier, Ligne, Bloc, Champ, ConversionResult...) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class ConversionResultContractTests
{
    // KAPE22-like profile: <header> + one <message> + <footer>, Segment control on. Champs are declared
    // in Position order; the last Champ of each Bloc is the trailing Filler / Reserve whose truncation
    // at the end of the Ligne is tolerated.
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

    // Message-only Descripteur: no Segment control, every Ligne is a <message>. Amount is a non-trailing
    // int Champ, so a non-numeric Valeur brute yields an InvalidInteger on that Ligne.
    private const string MessageOnlyTypedInt = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Marker" Position="0" Size="1" datatype="string" />
            <value Id="Amount" Position="1" Size="7" datatype="int" />
            <value Id="Tail" Position="8" Size="4" datatype="string" />
          </message>
        </commande>
        """;

    // Message-only Descripteur with three non-trailing Champs, so a short Ligne is too short for Mid.
    private const string MessageOnlyLineLength = """
        <?xml version="1.0" encoding="utf-8"?>
        <commande type="GEN" format="Fixed">
          <message type="GEN" index="0">
            <value Id="Alpha" Position="0" Size="3" datatype="string" />
            <value Id="Mid" Position="10" Size="3" datatype="string" />
            <value Id="Tail" Position="20" Size="3" datatype="string" />
          </message>
        </commande>
        """;

    // A malformed Descripteur, so Convert returns a single File-level LayoutInvalid.
    private const string MalformedDescriptor = "<commande type=\"GEN\" format=\"Fixed\"><message>";

    // A three-Ligne KAPE22 Fichier that matches HeaderMessageFooter and normalizes without any Error.
    private static byte[] ReferenceFichier() => Windows1252(string.Join(
        "\r\n",
        Row(25, (0, "P60"), (6, "001"), (9, "000")),
        Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
        Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

    // A three-Ligne KAPE22 Fichier whose Header and Footer Segment values do not match their markers
    // (Warnings on Lignes 1 and 3) and whose DiametreProduit is non-numeric (Error on Ligne 2).
    private static byte[] WarningsAndErrorFichier() => Windows1252(string.Join(
        "\r\n",
        Row(25, (0, "P60"), (6, "001"), (9, "XXX")),
        Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "12A4567")),
        Row(30, (0, "P60"), (9, "YYY"), (12, "00003"))));

    // AC-FR6-1: a successful conversion has an empty Errors list, a non-null Xml and that Xml is
    // well-formed. Warnings may still be present.
    [Fact]
    [Trait("AC", "FR6-1")]
    public void Convert_Success_HasNoErrorsAndWellFormedXml_AcFr6_1()
    {
        ConversionResult result = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Xml);

        XElement file = FileRoot(result.Xml!);
        Assert.Equal("file", file.Name.LocalName);
    }

    // AC-FR6-1 / AC-FR6-2: on both a success and a failure, Success tracks an empty Errors list and the
    // Xml is present exactly when Success is true.
    [Fact]
    [Trait("AC", "FR6-1")]
    public void Convert_SuccessAndXml_TrackTheErrorsList_AcFr6_1()
    {
        ConversionResult ok = Converter.Convert(ReferenceFichier(), HeaderMessageFooter);
        ConversionResult ko = Converter.Convert(Windows1252("A11A0   XXXX"), MessageOnlyTypedInt);

        AssertContractInvariants(ok);
        AssertContractInvariants(ko);
        Assert.True(ok.Success);
        Assert.False(ko.Success);
    }

    // AC-FR6-2: a failed conversion carries at least one Error and a null Xml.
    [Fact]
    [Trait("AC", "FR6-2")]
    public void Convert_Failure_HasAtLeastOneErrorAndNullXml_AcFr6_2()
    {
        ConversionResult result = Converter.Convert(Windows1252("A11A0   XXXX"), MessageOnlyTypedInt);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Xml);
    }

    // AC-FR6-2: the null-Xml guarantee holds for a failure raised at every pipeline stage.
    [Theory]
    [Trait("AC", "FR6-2")]
    [MemberData(nameof(FailuresAtEveryStage))]
    public void Convert_FailureAtAnyStage_YieldsNullXml_AcFr6_2(byte[] input, string descriptor)
    {
        ConversionResult result = Converter.Convert(input, descriptor);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Null(result.Xml);
    }

    // One failing (input, descriptor) pair per blocking stage: LayoutInvalid, EmptyFile,
    // UndecodableInput, WrongBlockCount, LineTooShort, InvalidInteger.
    public static IEnumerable<object[]> FailuresAtEveryStage()
    {
        yield return [Windows1252("anything"), MalformedDescriptor];
        yield return [Array.Empty<byte>(), MessageOnlyTypedInt];
        yield return [new byte[] { 0x81 }, MessageOnlyTypedInt];
        yield return [ReferenceFichier().Concat(Windows1252("\r\nP60")).ToArray(), HeaderMessageFooter];
        yield return [Windows1252("AB"), MessageOnlyLineLength];
        yield return [Windows1252("A11A0   XXXX"), MessageOnlyTypedInt];
    }

    // AC-FR6-3: a lone SegmentMismatch (no Error) keeps Success true, produces the Xml and carries
    // exactly one Warning.
    [Fact]
    [Trait("AC", "FR6-3")]
    public void Convert_SegmentMismatchAlone_SucceedsWithOneWarning_AcFr6_3()
    {
        // The Detail Segment reads "000" while the messageMarker is "EOF"; the Ligne count is correct.
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "000"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
            Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.True(result.Success);
        Assert.NotNull(result.Xml);
        ConversionError warning = Assert.Single(result.Warnings);
        Assert.Equal(ErrorCode.SegmentMismatch, warning.Code);
    }

    // AC-FR6-4: Errors are ordered by ascending LineNumber. A bad DiametreProduit on Ligne 2 and a bad
    // Records on Ligne 3 must come back in that order.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void Convert_SeveralErrors_AreOrderedByAscendingLineNumber_AcFr6_4()
    {
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "12A4567")),
            Row(30, (0, "P60"), (9, "999"), (12, "9A9A9"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.False(result.Success);
        Assert.Equal(new[] { 2, 3 }, result.Errors.Select(e => e.LineNumber).ToArray());
    }

    // AC-FR6-4: Warnings are ordered by ascending LineNumber. A Header Segment mismatch on Ligne 1 and a
    // Footer Segment mismatch on Ligne 3 must come back in that order.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void Convert_SeveralWarnings_AreOrderedByAscendingLineNumber_AcFr6_4()
    {
        byte[] fichier = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "XXX")),
            Row(60, (0, "P60"), (9, "EOF"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
            Row(30, (0, "P60"), (9, "YYY"), (12, "00003"))));

        ConversionResult result = Converter.Convert(fichier, HeaderMessageFooter);

        Assert.True(result.Success);
        Assert.Equal(new[] { 1, 3 }, result.Warnings.Select(w => w.LineNumber).ToArray());
    }

    // AC-FR6-4: Errors and Warnings are each sorted independently when a result carries both.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void Convert_ErrorsAndWarnings_AreEachSortedByLineNumber_AcFr6_4()
    {
        ConversionResult result = Converter.Convert(WarningsAndErrorFichier(), HeaderMessageFooter);

        Assert.False(result.Success);
        Assert.Equal(new[] { 2 }, result.Errors.Select(e => e.LineNumber).ToArray());
        Assert.Equal(new[] { 1, 3 }, result.Warnings.Select(w => w.LineNumber).ToArray());
        AssertNonDecreasing(result.Errors);
        AssertNonDecreasing(result.Warnings);
    }

    // AC-FR6-4: a File-level Error carries LineNumber 0, so under an ascending-LineNumber order it sits
    // at the head. A Step-1 failure that reaches this state carries a single File-level Error; a list
    // mixing File-level and Ligne-level Errors only becomes reachable in Step 2.
    [Fact]
    [Trait("AC", "FR6-4")]
    public void Convert_FileLevelError_CarriesLineNumberZero_AcFr6_4()
    {
        ConversionResult result = Converter.Convert(Array.Empty<byte>(), MessageOnlyTypedInt);

        Assert.False(result.Success);
        AssertNonDecreasing(result.Errors);
        Assert.Equal(0, result.Errors[0].LineNumber);
    }

    // AC-FR6-5: every Error Message is non-blank, reads as French, and is free of a stack trace or a
    // leaked .NET exception or type name, across a failure at every blocking stage.
    [Theory]
    [Trait("AC", "FR6-5")]
    [MemberData(nameof(FailuresAtEveryStage))]
    public void Convert_ErrorMessages_AreCleanFrenchText_AcFr6_5(byte[] input, string descriptor)
    {
        ConversionResult result = Converter.Convert(input, descriptor);

        Assert.NotEmpty(result.Errors);
        foreach (ConversionError error in result.Errors)
        {
            AssertCleanFrenchMessage(error.Message);
        }
    }

    // AC-FR6-5: Warning Messages are held to the same rule as Error Messages.
    [Fact]
    [Trait("AC", "FR6-5")]
    public void Convert_WarningMessages_AreCleanFrenchText_AcFr6_5()
    {
        ConversionResult result = Converter.Convert(WarningsAndErrorFichier(), HeaderMessageFooter);

        Assert.NotEmpty(result.Warnings);
        foreach (ConversionError warning in result.Warnings)
        {
            AssertCleanFrenchMessage(warning.Message);
        }
    }

    // AC-FR6-6: Convert never throws for 20 generated corrupt inputs (random bytes, sizes 0..2000).
    // A fixed seed keeps the run reproducible.
    [Fact]
    [Trait("AC", "FR6-6")]
    public void Convert_TwentyFuzzedInputs_NeverThrow_AcFr6_6()
    {
        Random random = new(20260903);

        for (int i = 0; i < 20; i++)
        {
            byte[] input = new byte[random.Next(0, 2001)];
            random.NextBytes(input);

            ConversionResult headerResult = Converter.Convert(input, HeaderMessageFooter);
            ConversionResult messageResult = Converter.Convert(input, MessageOnlyTypedInt);

            // Reaching this line already proves no exception escaped; assert the contract invariants
            // hold on each corrupt input as well (AC-FR6-1, AC-FR6-2).
            AssertContractInvariants(headerResult);
            AssertContractInvariants(messageResult);
        }
    }

    // AC-FR6-6: a corrupt input that decodes but is structurally broken still comes back as a result,
    // not an exception.
    [Fact]
    [Trait("AC", "FR6-6")]
    public void Convert_UndecodableByte_IsReportedNotThrown_AcFr6_6()
    {
        ConversionResult result = Converter.Convert(new byte[] { 0x41, 0x81, 0x42 }, MessageOnlyTypedInt);

        Assert.False(result.Success);
        Assert.Equal(ErrorCode.UndecodableInput, Assert.Single(result.Errors).Code);
    }

    // AC-FR6-7: ConversionError round-trips through System.Text.Json with no configuration.
    [Fact]
    [Trait("AC", "FR6-7")]
    public void ConversionError_RoundTripsThroughSystemTextJson_AcFr6_7()
    {
        ConversionError original = new()
        {
            Block = Block.Detail,
            Code = ErrorCode.InvalidInteger,
            Column = "DiametreProduit",
            FieldId = "DiametreProduit",
            LineNumber = 2,
            Message = "La valeur « 12A4567 » du Champ « DiametreProduit » n'est pas un entier non signé valide.",
            RawValue = "12A4567",
        };

        string json = JsonSerializer.Serialize(original);
        ConversionError roundTripped = JsonSerializer.Deserialize<ConversionError>(json)!;

        Assert.Equal(original, roundTripped);
    }

    // AC-FR6-7: the Errors carried by a real failed conversion serialize to JSON without configuration.
    [Fact]
    [Trait("AC", "FR6-7")]
    public void ConversionResultErrors_SerializeToJson_AcFr6_7()
    {
        ConversionResult result = Converter.Convert(WarningsAndErrorFichier(), HeaderMessageFooter);

        string json = JsonSerializer.Serialize(result.Errors);
        List<ConversionError> roundTripped = JsonSerializer.Deserialize<List<ConversionError>>(json)!;

        Assert.Equal(result.Errors, roundTripped);
    }

    // NFR-3: 100 concurrent Convert calls on varied inputs produce results identical to the sequential run.
    [Fact]
    [Trait("NFR", "3")]
    public void Convert_HundredConcurrentCalls_MatchSequentialResults_Nfr3()
    {
        (byte[] Input, string Descriptor)[] cases = ConcurrencyCases();

        ConversionResult[] sequential = cases
            .Select(c => Converter.Convert(c.Input, c.Descriptor))
            .ToArray();

        ConversionResult[] concurrent = new ConversionResult[cases.Length];
        Parallel.For(0, cases.Length, i =>
        {
            concurrent[i] = Converter.Convert(cases[i].Input, cases[i].Descriptor);
        });

        for (int i = 0; i < cases.Length; i++)
        {
            Assert.Equal(sequential[i].Success, concurrent[i].Success);
            Assert.Equal(sequential[i].Xml, concurrent[i].Xml, StringComparer.Ordinal);
            Assert.Equal(sequential[i].Errors, concurrent[i].Errors);
            Assert.Equal(sequential[i].Warnings, concurrent[i].Warnings);
        }
    }

    // 100 (input, descriptor) pairs cycling through success, Segment Warning, InvalidInteger, EmptyFile
    // and LayoutInvalid, so the concurrent run exercises every pipeline outcome at once.
    private static (byte[] Input, string Descriptor)[] ConcurrencyCases()
    {
        byte[] segmentWarning = Windows1252(string.Join(
            "\r\n",
            Row(25, (0, "P60"), (6, "001"), (9, "000")),
            Row(60, (0, "P60"), (9, "000"), (21, "E"), (22, "0397710"), (29, "1"), (30, "APERAM ALLOYS"), (45, "0005900")),
            Row(30, (0, "P60"), (9, "999"), (12, "00003"))));

        (byte[] Input, string Descriptor)[] palette =
        [
            (ReferenceFichier(), HeaderMessageFooter),
            (segmentWarning, HeaderMessageFooter),
            (Windows1252("A11A0   XXXX"), MessageOnlyTypedInt),
            (Array.Empty<byte>(), MessageOnlyTypedInt),
            (Windows1252("anything"), MalformedDescriptor),
        ];

        return Enumerable.Range(0, 100)
            .Select(i => palette[i % palette.Length])
            .ToArray();
    }

    // NFR-1: Convert alone must sit far below the 200 ms end-to-end budget for a ~700-byte, 3-Ligne
    // Fichier. This is an indicative regression guard, not the budget itself; the 50 ms per-call
    // ceiling is deliberately loose so the check does not flake on a loaded runner while still
    // catching a gross regression.
    [Fact]
    [Trait("NFR", "1")]
    public void Convert_ReferenceFichier_StaysFarBelowEndToEndBudget_Nfr1()
    {
        byte[] fichier = ReferenceFichier();
        const double indicativeCeilingMs = 50;

        // Warm up the descriptor parsing and the JIT before measuring.
        for (int i = 0; i < 20; i++)
        {
            Converter.Convert(fichier, HeaderMessageFooter);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        const int iterations = 200;
        for (int i = 0; i < iterations; i++)
        {
            Converter.Convert(fichier, HeaderMessageFooter);
        }

        stopwatch.Stop();

        double averageMs = stopwatch.Elapsed.TotalMilliseconds / iterations;
        Assert.True(
            averageMs < indicativeCeilingMs,
            $"Convert averaged {averageMs:F2} ms per call, above the {indicativeCeilingMs} ms indicative "
            + "ceiling (the NFR-1 end-to-end budget is 200 ms).");
    }

    // French guillemets and spaced French function words that a French Message carries and an English
    // regression (for example "The file is empty.") would not.
    private static readonly string[] FrenchMarkers =
        ["«", " le ", " la ", " ne ", " pas ", " de ", " un ", " est ", " dans "];

    // Tokens from framework XmlException / exception text; none appears in a hand-written French Message.
    private static readonly string[] EnglishLeakTokens =
        ["occurred", "unexpected", "the following", "not closed", "was expected", "is expected", "line "];

    // The frozen contract: Success is exactly "no Error", and the Xml is present exactly on success
    // (AC-FR6-1, AC-FR6-2).
    private static void AssertContractInvariants(ConversionResult result)
    {
        Assert.Equal(result.Errors.Count == 0, result.Success);
        Assert.Equal(result.Success, result.Xml is not null);
    }

    // A ConversionError Message must be non-blank, read as French, and be free of a stack trace or a
    // leaked .NET exception or type name (AC-FR6-5).
    private static void AssertCleanFrenchMessage(string message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));

        // A stack trace or a leaked exception carries one of these tokens; a clean Message does not.
        Assert.DoesNotContain("Exception", message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("TextToXml.", message, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', message);
        Assert.DoesNotContain('\r', message);

        // English words that only reach a Message when framework exception text is passed through
        // verbatim; our own wording never uses them (heuristic, extend as new leaks are found).
        foreach (string englishLeak in EnglishLeakTokens)
        {
            Assert.DoesNotContain(englishLeak, message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(
            FrenchMarkers.Any(marker => message.Contains(marker, StringComparison.Ordinal)),
            $"Message does not read as French: {message}");
    }

    private static void AssertNonDecreasing(IReadOnlyList<ConversionError> entries)
    {
        for (int i = 1; i < entries.Count; i++)
        {
            Assert.True(
                entries[i - 1].LineNumber <= entries[i].LineNumber,
                $"Entry {i} (LineNumber {entries[i].LineNumber}) precedes {entries[i - 1].LineNumber}.");
        }
    }
}
