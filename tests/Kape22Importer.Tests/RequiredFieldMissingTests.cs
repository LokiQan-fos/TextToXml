using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.5 (AC-FR8-5, AC-FR8-6): the per-file half of FR-8. RequiredFieldCheck.Check walks the
// deserialized Detail block and returns one RequiredFieldMissing per NOT NULL column left null or
// blank by Step 1, ordered by the Descripteur Champ order. Written test-first (CC-1), Unit-only: the
// tests convert a reference Fichier, blank a Champ in the normalized XML, and inspect the errors.
[Trait("Category", TestCategory.Unit)]
public class RequiredFieldMissingTests
{
    private const string ReferenceFichierName = "P60_847_682_001";

    // AC-FR8-5: a NOT NULL Detail column blanked by Step 1 (here Coulee) yields exactly one
    // RequiredFieldMissing naming the Bloc, the Champ and the column.
    [Fact]
    [Trait("AC", "FR8-5")]
    public void Check_BlankNotNullDetailChamp_YieldsRequiredFieldMissing_AcFr8_5()
    {
        Kape22File file = Deserialize(NormalizedXmlWithBlankChamps("Coulee"));

        ConversionError error = Assert.Single(RequiredFieldCheck.Check(file));

        Assert.Equal(ErrorCode.RequiredFieldMissing, error.Code);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal("Coulee", error.FieldId);
        Assert.Equal("Coulee", error.Column);
    }

    // AC-FR8-5: a fully valid Fichier yields no RequiredFieldMissing.
    [Fact]
    [Trait("AC", "FR8-5")]
    public void Check_ValidFichier_YieldsNoRequiredFieldMissing_AcFr8_5()
    {
        Kape22File file = Deserialize(ConvertReferenceFichier());

        Assert.Empty(RequiredFieldCheck.Check(file));
    }

    // AC-FR8-5: a blank typed (int) NOT NULL Detail column (here Indice) yields one RequiredFieldMissing
    // just like a blank string column. For a typed Champ, Step 1 omits the element, so "blank" is a null
    // DTO value.
    [Fact]
    [Trait("AC", "FR8-5")]
    public void Check_MissingTypedNotNullChamp_YieldsRequiredFieldMissing_AcFr8_5()
    {
        Kape22File file = Deserialize(NormalizedXmlWithoutChamps("Indice"));

        ConversionError error = Assert.Single(RequiredFieldCheck.Check(file));

        Assert.Equal(ErrorCode.RequiredFieldMissing, error.Code);
        Assert.Equal(Block.Detail, error.Block);
        Assert.Equal("Indice", error.Column);
    }

    // AC-FR8-5: NumeroFichier is a NOT NULL column filled by an FR-9 derived rule (the Header roulette),
    // not the Detail Champ that happens to share its name, so blanking that Champ produces no error.
    [Fact]
    [Trait("AC", "FR8-5")]
    public void Check_BlankDerivedColumnChamp_YieldsNoRequiredFieldMissing_AcFr8_5()
    {
        Kape22File file = Deserialize(NormalizedXmlWithBlankChamps("NumeroFichier"));

        Assert.Empty(RequiredFieldCheck.Check(file));
    }

    // AC-FR8-6: several blank NOT NULL columns produce one error per column, and the list is ordered by
    // the Descripteur Champ order (Type at Position 21, then Nuance at 43, then Coulee at 50).
    [Fact]
    [Trait("AC", "FR8-6")]
    public void Check_MultipleBlankNotNullChamps_OneErrorPerColumnInDescriptorOrder_AcFr8_6()
    {
        Kape22File file = Deserialize(NormalizedXmlWithBlankChamps("Coulee", "Nuance", "Type"));

        IReadOnlyList<ConversionError> errors = RequiredFieldCheck.Check(file);

        Assert.Equal(new[] { "Type", "Nuance", "Coulee" }, errors.Select(error => error.Column).ToArray());
        Assert.All(errors, error => Assert.Equal(ErrorCode.RequiredFieldMissing, error.Code));
    }

    // Converts the reference Fichier, then empties the text content of the named message Champs so the
    // deserialized DTO carries blank NOT NULL values.
    private static string NormalizedXmlWithBlankChamps(params string[] champIds)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        XElement message = document.Root!.Element("message")!;

        foreach (string champId in champIds)
        {
            message.Element(champId)!.Value = string.Empty;
        }

        return document.ToString();
    }

    // Converts the reference Fichier, then removes the named message Champs entirely, mirroring how
    // Step 1 omits the element of a blank typed Champ (PRD D27).
    private static string NormalizedXmlWithoutChamps(params string[] champIds)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        XElement message = document.Root!.Element("message")!;

        foreach (string champId in champIds)
        {
            message.Element(champId)?.Remove();
        }

        return document.ToString();
    }

    private static Kape22File Deserialize(string normalizedXml)
    {
        P60DeserializeResult result = P60Deserializer.Deserialize(normalizedXml);
        Assert.Empty(result.Errors);
        return result.File!;
    }

    private static string ConvertReferenceFichier()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture(ReferenceFichierName), EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, "reference fixture failed to convert.");
        return conversion.Xml!;
    }

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    private static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));
}
