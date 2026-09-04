using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static TextToXml.Tests.TestSupport;

namespace TextToXml.Tests;

// Story 1.8 - Genericity: TextToXml runs on a synthetic Descripteur whose layout is nothing like P60,
// with no change to the library (FR-1, FR-5, FR-16).
// These are genericity-proof tests: the library was already format-agnostic after Stories 1.2 and 1.6,
// so they pass on creation, and their value is as a guard - a later change that leaked a P60 assumption
// into the engine would turn them red. CC-1 exempts this shape from the clean red-to-green ceremony,
// like the architecture tests in FormatIsolationTests.
// Vocabulary follows the PRD glossary section 3 (Descripteur, Champ, Bloc, XML normalisé) (CC-5).
[Trait("Category", TestCategory.Unit)]
public class GenericFormatTests
{
    // Structural tokens and Champ names that only make sense for the P60 / KAPE22 layout; none may leak
    // into a conversion driven by a different Descripteur. Bare numeric values (transmission numbers and
    // the like) are deliberately excluded - they collide with legitimate data.
    private static readonly string[] P60SpecificTokens =
        ["P60", "KAPE22", "EOF", "Segment", "DiametreProduit", "Coulee", "Records"];

    // AC-FR1-9: a synthetic Descripteur with different Ids, Positions, Sizes and no header/footer is
    // converted with no change to TextToXml; the XML is coherent with that Descripteur.
    [Fact]
    [Trait("AC", "FR1-9")]
    public void Convert_SyntheticMessageOnlyDescriptor_ProducesCoherentXml_AcFr1_9()
    {
        ConversionResult result = Converter.Convert(ReadInput("message-only.txt"), ReadDescriptor("message-only.xml"));

        Assert.True(result.Success);
        Assert.NotNull(result.Xml);

        XElement file = FileRoot(result.Xml!);
        Assert.Equal("file", file.Name.LocalName);
        Assert.Empty(file.Elements("header"));
        Assert.Empty(file.Elements("footer"));
        Assert.Equal(3, file.Elements("message").Count());

        XElement firstMessage = file.Elements("message").First();
        Assert.Equal(new[] { "Ref", "Label", "Quantity" }, firstMessage.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("W001", firstMessage.Element("Ref")!.Value);
        Assert.Equal("Left widget", firstMessage.Element("Label")!.Value);
        Assert.Equal("42", firstMessage.Element("Quantity")!.Value);
    }

    // AC-FR5-13: element names are exactly the Ids of the running Descripteur and no P60-specific tag
    // appears anywhere in the produced XML.
    [Fact]
    [Trait("AC", "FR5-13")]
    public void Convert_SyntheticDescriptor_EmitsOnlyItsOwnIdsAndNoP60Tag_AcFr5_13()
    {
        ConversionResult result = Converter.Convert(ReadInput("message-only.txt"), ReadDescriptor("message-only.xml"));

        Assert.True(result.Success);

        string[] elementNames = FileRoot(result.Xml!)
            .DescendantsAndSelf()
            .Select(e => e.Name.LocalName)
            .Distinct()
            .ToArray();
        Assert.Equal(new[] { "file", "message", "Ref", "Label", "Quantity" }, elementNames);

        foreach (string token in P60SpecificTokens)
        {
            Assert.DoesNotContain(token, result.Xml!, StringComparison.Ordinal);
        }
    }

    // AC-FR5-13: the last <message> reflects the last Ligne, proving every Ligne is mapped through the
    // synthetic Descripteur and not through a P60 assumption of a single detail Ligne.
    [Fact]
    [Trait("AC", "FR5-13")]
    public void Convert_SyntheticDescriptor_MapsEveryLigne_AcFr5_13()
    {
        ConversionResult result = Converter.Convert(ReadInput("message-only.txt"), ReadDescriptor("message-only.xml"));

        Assert.True(result.Success);

        XElement lastMessage = FileRoot(result.Xml!).Elements("message").Last();
        Assert.Equal("W003", lastMessage.Element("Ref")!.Value);
        Assert.Equal("Spare part", lastMessage.Element("Label")!.Value);
        Assert.Equal("1000", lastMessage.Element("Quantity")!.Value);
    }
}
