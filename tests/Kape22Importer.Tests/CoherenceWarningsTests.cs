using System;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 2.7 (FR-10): Kape22Mapper.Map receives the Fichier name and raises the three coherence checks
// as non-blocking Warnings - Footer.Records != 3 (D18), the File Champ diverging between the three
// Blocs, and a name segment diverging from its Entete homonym. Written test-first (CC-1): red until the
// coherence checks and MapResult.Warnings ship. Unit-only (AR-12), no database: the mapper never
// touches EF. Vocabulary follows the PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class CoherenceWarningsTests
{
    // AC-FR10-1 (D18): Footer.Records counts 3 = Entete + message + Pied; any other value is a
    // Warning {Block:Footer, FieldId:"Records", Code:InterBlockMismatch}, and the Fichier is still
    // mapped to an entity.
    [Fact]
    [Trait("AC", "FR10-1")]
    public void Map_FooterRecordsNotThree_YieldsInterBlockMismatchWarning_AcFr10_1()
    {
        string xml = NormalizedReferenceXml(document =>
            document.Root!.Element("footer")!.Element("Records")!.Value = "00009");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        ConversionError warning = Assert.Single(
            result.Warnings, candidate => candidate.FieldId == "Records");
        Assert.Equal(ErrorCode.InterBlockMismatch, warning.Code);
        Assert.Equal(Block.Footer, warning.Block);
        Assert.Equal("00009", warning.RawValue);
    }

    // AC-FR10-1: a non-numeric Records is also "not 3" - still a Warning, never a blocking error.
    [Fact]
    [Trait("AC", "FR10-1")]
    public void Map_FooterRecordsNonNumeric_YieldsInterBlockMismatchWarningNotError_AcFr10_1()
    {
        string xml = NormalizedReferenceXml(document =>
            document.Root!.Element("footer")!.Element("Records")!.Value = "abc");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Contains(
            result.Warnings,
            candidate => candidate.FieldId == "Records" && candidate.Code == ErrorCode.InterBlockMismatch);
    }

    // AC-FR10-2: the reference Fichier carries Records "00003", so Map raises no Records Warning.
    [Fact]
    [Trait("AC", "FR10-2")]
    public void Map_FooterRecordsThree_RaisesNoRecordsWarning_AcFr10_2()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, candidate => candidate.FieldId == "Records");
    }

    // AC-FR10-2: leading zeros do not matter - a bare "3" is still three.
    [Fact]
    [Trait("AC", "FR10-2")]
    public void Map_FooterRecordsUnpaddedThree_RaisesNoRecordsWarning_AcFr10_2()
    {
        string xml = NormalizedReferenceXml(document =>
            document.Root!.Element("footer")!.Element("Records")!.Value = "3");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.DoesNotContain(result.Warnings, candidate => candidate.FieldId == "Records");
    }

    // AC-FR10-3: the File Champ (Position 0, Size 3) present in the three Blocs must agree; when one
    // Bloc diverges the mapper raises Warning {Block:File, FieldId:"File", Code:InterBlockMismatch}.
    [Fact]
    [Trait("AC", "FR10-3")]
    public void Map_FileChampDiffersBetweenBlocs_YieldsInterBlockMismatchWarning_AcFr10_3()
    {
        string xml = NormalizedReferenceXml(document =>
            document.Root!.Element("message")!.Element("File")!.Value = "X60");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        ConversionError warning = Assert.Single(
            result.Warnings, candidate => candidate.FieldId == "File");
        Assert.Equal(ErrorCode.InterBlockMismatch, warning.Code);
        Assert.Equal(Block.File, warning.Block);
    }

    // AC-FR10-3: the three Blocs of the reference Fichier all carry File "P60", so no File Warning.
    [Fact]
    [Trait("AC", "FR10-3")]
    public void Map_FileChampConsistentAcrossBlocs_RaisesNoFileWarning_AcFr10_3()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

        Assert.DoesNotContain(result.Warnings, candidate => candidate.FieldId == "File");
    }

    // AC-FR10-4: the name P60_847_682_001 decomposes into File / Emet / Recepteur / NumeroFichier; a
    // segment differing from the homonym Champ of the Entete is a Warning {Block:File, FieldId:"<champ>",
    // Code:FileNameMismatch, RawValue:"<name segment>"}.
    [Theory]
    [InlineData("Z60_847_682_001", "File", "Z60")]
    [InlineData("P60_999_682_001", "Emet", "999")]
    [InlineData("P60_847_111_001", "Recepteur", "111")]
    [InlineData("P60_847_682_777", "NumeroFichier", "777")]
    [Trait("AC", "FR10-4")]
    public void Map_NameSegmentDiffersFromHeaderHomonym_YieldsFileNameMismatchWarning_AcFr10_4(
        string sourceFileName, string expectedFieldId, string expectedRawValue)
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), sourceFileName, WinterClock());

        Assert.True(result.Success);
        ConversionError warning = Assert.Single(
            result.Warnings, candidate => candidate.Code == ErrorCode.FileNameMismatch);
        Assert.Equal(Block.File, warning.Block);
        Assert.Equal(expectedFieldId, warning.FieldId);
        Assert.Equal(expectedRawValue, warning.RawValue);
    }

    // AC-FR10-4: leading zeros are ignored for the NumeroFichier comparison only - name segment "1"
    // matches the Entete roulette "001".
    [Fact]
    [Trait("AC", "FR10-4")]
    public void Map_NumeroFichierNameSegmentDiffersOnlyByLeadingZeros_RaisesNoWarning_AcFr10_4()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), "P60_847_682_1", WinterClock());

        Assert.DoesNotContain(result.Warnings, candidate => candidate.Code == ErrorCode.FileNameMismatch);
    }

    // AC-FR10-5: a name outside the A_B_C_D pattern (exactly three underscores) is a single Warning
    // {Block:File, Code:FileNameMismatch} citing the name; a trailing .txt extension is ignored.
    [Theory]
    [InlineData("P60_847_682")]
    [InlineData("P60_847_682_001_extra")]
    [InlineData("P60847682001")]
    [Trait("AC", "FR10-5")]
    public void Map_NameOutsideExpectedPattern_YieldsSingleFileNameMismatchCitingName_AcFr10_5(
        string sourceFileName)
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), sourceFileName, WinterClock());

        Assert.True(result.Success);
        ConversionError warning = Assert.Single(result.Warnings);
        Assert.Equal(ErrorCode.FileNameMismatch, warning.Code);
        Assert.Equal(Block.File, warning.Block);
        Assert.Equal(sourceFileName, warning.RawValue);
    }

    // AC-FR10-5: the .txt extension is stripped before the pattern and segment comparison, so a
    // well-formed name with that extension matches the Entete and raises nothing.
    [Fact]
    [Trait("AC", "FR10-5")]
    public void Map_WellFormedNameWithTxtExtension_RaisesNoWarning_AcFr10_5()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), "P60_847_682_001.txt", WinterClock());

        Assert.Empty(result.Warnings);
    }

    // AC-FR10-6: name and Entete concordant - the reference Fichier and its own name raise no Warning
    // at all.
    [Fact]
    [Trait("AC", "FR10-6")]
    public void Map_NameAndHeaderConcordant_RaisesNoWarning_AcFr10_6()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    // AC-FR10-7 (mapper half): a Fichier whose only defects are coherence Warnings, with otherwise
    // valid data, still maps to an entity - Success stays true, Value is produced, Errors is empty.
    // The persistence + MQTTnetServices.Logs half of AC-FR10-7 is a Story 2.8 integration concern.
    [Fact]
    [Trait("AC", "FR10-7")]
    public void Map_FichierWithOnlyCoherenceWarnings_StillProducesEntity_AcFr10_7()
    {
        string xml = NormalizedReferenceXml(document =>
        {
            document.Root!.Element("footer")!.Element("Records")!.Value = "00009";
            document.Root!.Element("message")!.Element("File")!.Value = "X60";
        });

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, "P60_999_682_001", WinterClock());

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Value);
        Assert.Equal(3, result.Warnings.Count);
    }

    // AC-FR10-7: a coherence Warning never turns into a blocking error - Success and Value are the same
    // as for the clean reference Fichier.
    [Fact]
    [Trait("AC", "FR10-7")]
    public void Map_CoherenceWarnings_DoNotAffectErrorsOrSuccess_AcFr10_7()
    {
        string xml = NormalizedReferenceXml(document =>
            document.Root!.Element("footer")!.Element("Records")!.Value = "42");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    // Converts the reference Fichier, applies a mutation to the normalized XML, and returns the result.
    private static string NormalizedReferenceXml(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return document.ToString();
    }
}
