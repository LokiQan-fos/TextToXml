using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.6 (FR-9): Kape22Mapper.Map applies the P60-specific derived rules on top of the Annexe B
// property copy - day-of-year Header.Date (D4), roulette NumeroFichier, worker DateReception, blank
// Indice rejection, and the ignored DateEnfournementFour1/2 slices (D14). Written test-first (CC-1):
// red until the derived rules ship. Unit-only (AR-12), no database: the mapper never touches EF.
// Vocabulary follows the PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class DerivedFieldsTests
{
    private const string ReferenceFichierName = "P60_847_682_001";

    // AC-FR9-1: "245" is day 245 of the current year, and the current year is read from the Paris
    // wall clock, not from UTC - so a UTC instant that is already the next year in Paris derives the
    // Paris year.
    [Theory]
    [InlineData("245", "2026-06-01T12:00:00Z", "2026-09-02")]
    [InlineData("001", "2026-06-01T12:00:00Z", "2026-01-01")]
    [InlineData("365", "2026-06-01T12:00:00Z", "2026-12-31")]
    [InlineData(" 45 ", "2026-06-01T12:00:00Z", "2026-02-14")]
    [InlineData("060", "2024-06-01T12:00:00Z", "2024-02-29")]
    [InlineData("366", "2024-06-01T12:00:00Z", "2024-12-31")]
    [InlineData("245", "2025-12-31T23:30:00Z", "2026-09-02")]
    [Trait("AC", "FR9-1")]
    public void TryConvertHeaderDate_ValidDayNumber_ReturnsDateInParisYear_AcFr9_1(
        string raw, string utcNow, string expectedDate)
    {
        TimeProvider clock = new FixedTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture));

        bool converted = Kape22Mapper.TryConvertHeaderDate(raw, clock, out DateTime date);

        Assert.True(converted);
        Assert.Equal(DateTime.Parse(expectedDate, CultureInfo.InvariantCulture), date);
    }

    // AC-FR9-1: "000", any value past the length of the current Paris year (here 2026, non-leap, so
    // "366" is already out of range), and a non-numeric or blank Champ all fail the conversion (Map
    // turns that failure into an InvalidDate error).
    [Theory]
    [InlineData("000")]
    [InlineData("366")]
    [InlineData("367")]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("AC", "FR9-1")]
    public void TryConvertHeaderDate_ZeroOutOfRangeOrNonNumeric_ReturnsFalse_AcFr9_1(string raw)
    {
        TimeProvider clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-06-01T12:00:00Z", CultureInfo.InvariantCulture));

        Assert.False(Kape22Mapper.TryConvertHeaderDate(raw, clock, out _));
    }

    // AC-FR9-1: a Header.Date the mapper cannot convert produces exactly one InvalidDate error, on the
    // Header Date Champ, and no entity.
    [Fact]
    [Trait("AC", "FR9-1")]
    public void Map_HeaderDateZero_YieldsSingleInvalidDateOnHeaderDate_AcFr9_1()
    {
        string xml = NormalizedXmlWithHeaderDate("000");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.False(result.Success);
        Assert.Null(result.Value);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.InvalidDate, error.Code);
        Assert.Equal(Block.Header, error.Block);
        Assert.Equal("Date", error.FieldId);
        Assert.Equal(1, error.LineNumber);
        Assert.Equal("000", error.RawValue);
    }

    // AC-FR9-1: the reference Fichier carries a valid Header.Date ("200"), so Map raises no InvalidDate.
    [Fact]
    [Trait("AC", "FR9-1")]
    public void Map_ValidReferenceFichier_RaisesNoInvalidDate_AcFr9_1()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

        Assert.DoesNotContain(result.Errors, error => error.Code == ErrorCode.InvalidDate);
    }

    // AC-FR9-1 and AC-FR9-4: a Fichier with several problems accumulates one ConversionError per
    // problem - a bad Header.Date and a blank Detail Indice both survive into the same MapResult, and
    // no entity is produced.
    [Fact]
    [Trait("AC", "FR9-1")]
    public void Map_HeaderDateInvalidAndIndiceBlank_YieldsBothErrors_AcFr9_1()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("header")!.Element("Date")!.Value = "000";
        document.Root!.Element("message")!.Element("Indice")!.Remove();

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(document.ToString(), ReferenceFichierName, WinterClock());

        Assert.False(result.Success);
        Assert.Null(result.Value);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.InvalidDate && error.Block == Block.Header);
        Assert.Contains(result.Errors, error => error.Code == ErrorCode.RequiredFieldMissing && error.Column == "Indice");
    }

    // AC-FR9-2: a blank Header roulette Champ rejects the Fichier - NumeroFichier is NOT NULL and half
    // the D22 anti-duplicate key, so it must not reach the insert empty. RequiredFieldCheck skips it
    // as a derived column, so Map owns the check.
    [Fact]
    [Trait("AC", "FR9-2")]
    public void Map_BlankHeaderNumeroFichier_YieldsRequiredFieldMissing_AcFr9_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("header")!.Element("NumeroFichier")!.Value = "   ";

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(document.ToString(), ReferenceFichierName, WinterClock());

        Assert.False(result.Success);
        Assert.Null(result.Value);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.RequiredFieldMissing, error.Code);
        Assert.Equal(Block.Header, error.Block);
        Assert.Equal("NumeroFichier", error.Column);
    }

    // AC-FR9-2: entity.NumeroFichier comes from the Header roulette Champ, not the Detail Champ that
    // shares its name; for the reference Fichier it equals the fourth name segment.
    [Fact]
    [Trait("AC", "FR9-2")]
    public void Map_ValidFichier_NumeroFichierComesFromHeaderRoulette_AcFr9_2()
    {
        string normalizedXml = ConvertReferenceFichier();
        string headerRoulette = (string)XDocument.Parse(normalizedXml).Root!.Element("header")!.Element("NumeroFichier")!;

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(normalizedXml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Equal(headerRoulette, result.Value!.NumeroFichier);
        Assert.Equal(ReferenceFichierName.Split('_')[3], result.Value.NumeroFichier);
    }

    // AC-FR9-2: the Detail NumeroFichier Champ is not the source - changing it does not change the entity.
    [Fact]
    [Trait("AC", "FR9-2")]
    public void Map_DetailNumeroFichierDiffersFromHeader_EntityKeepsHeaderValue_AcFr9_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        string headerRoulette = (string)document.Root!.Element("header")!.Element("NumeroFichier")!;
        document.Root!.Element("message")!.Element("NumeroFichier")!.Value = "999";

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(document.ToString(), ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Equal(headerRoulette, result.Value!.NumeroFichier);
    }

    // AC-FR9-3: DateReception is the worker processing timestamp in Paris time, taken from the injected
    // TimeProvider - here a winter instant, so Paris is UTC+1.
    [Fact]
    [Trait("AC", "FR9-3")]
    public void Map_ValidFichier_DateReceptionIsParisWinterTimestamp_AcFr9_3()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(
            ConvertReferenceFichier(),
            ReferenceFichierName,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-02-10T08:00:00Z", CultureInfo.InvariantCulture)));

        Assert.True(result.Success);
        Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), result.Value!.DateReception);
    }

    // AC-FR9-3: the same rule under summer time, so Paris is UTC+2 - proves the conversion honours DST
    // rather than adding a fixed offset.
    [Fact]
    [Trait("AC", "FR9-3")]
    public void Map_ValidFichier_DateReceptionHonoursParisSummerTime_AcFr9_3()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(
            ConvertReferenceFichier(),
            ReferenceFichierName,
            new FixedTimeProvider(DateTimeOffset.Parse("2026-07-10T08:00:00Z", CultureInfo.InvariantCulture)));

        Assert.True(result.Success);
        Assert.Equal(new DateTime(2026, 7, 10, 10, 0, 0), result.Value!.DateReception);
    }

    // AC-FR9-4: a blank Detail Indice (element omitted by Step 1 for a typed Champ) is a rejected
    // Fichier - one RequiredFieldMissing on the Indice column, and no entity.
    [Fact]
    [Trait("AC", "FR9-4")]
    public void Map_BlankIndice_YieldsRequiredFieldMissing_AcFr9_4()
    {
        string xml = NormalizedXmlWithoutDetailChamps("Indice");

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(xml, ReferenceFichierName, WinterClock());

        Assert.False(result.Success);
        Assert.Null(result.Value);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.RequiredFieldMissing, error.Code);
        Assert.Equal("Indice", error.Column);
    }

    // AC-FR9-4: a present Indice lands on the entity as the typed value from the Detail Champ.
    [Fact]
    [Trait("AC", "FR9-4")]
    public void Map_ValidFichier_IndiceComesFromDetailChamp_AcFr9_4()
    {
        string normalizedXml = ConvertReferenceFichier();
        int expected = int.Parse(
            (string)XDocument.Parse(normalizedXml).Root!.Element("message")!.Element("Indice")!,
            CultureInfo.InvariantCulture);

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(normalizedXml, ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Equal(expected, result.Value!.Indice);
    }

    // AC-FR9-5 (D14): the reference Fichier leaves both DateEnfournement columns NULL.
    [Fact]
    [Trait("AC", "FR9-5")]
    public void Map_ValidReferenceFichier_LeavesDateEnfournementColumnsNull_AcFr9_5()
    {
        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Null(result.Value!.DateEnfournementFour1);
        Assert.Null(result.Value.DateEnfournementFour2);
    }

    // AC-FR9-5 (D14): even when the four DateEnfournementFour1/2 _Date / _Heure Champs carry values,
    // the mapper ignores them and the columns stay NULL - the slices are never recombined into a
    // DateTime.
    [Fact]
    [Trait("AC", "FR9-5")]
    public void Map_PopulatedDateEnfournementChamps_StillLeavesColumnsNull_AcFr9_5()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        XElement message = document.Root!.Element("message")!;
        message.Element("DateEnfournementFour1_Date")!.Value = "0102";
        message.Element("DateEnfournementFour1_Heure")!.Value = "0930";
        message.Element("DateEnfournementFour2_Date")!.Value = "0304";
        message.Element("DateEnfournementFour2_Heure")!.Value = "1145";

        MapResult<L_D_KAPE22> result = Kape22Mapper.Map(document.ToString(), ReferenceFichierName, WinterClock());

        Assert.True(result.Success);
        Assert.Null(result.Value!.DateEnfournementFour1);
        Assert.Null(result.Value.DateEnfournementFour2);
    }

    // AC-FR9-5: the four DateEnfournement slices are all in the mapper's ignored set (Annexe B, D14).
    [Theory]
    [InlineData("DateEnfournementFour1_Date")]
    [InlineData("DateEnfournementFour1_Heure")]
    [InlineData("DateEnfournementFour2_Date")]
    [InlineData("DateEnfournementFour2_Heure")]
    [Trait("AC", "FR9-5")]
    public void IgnoredProperties_ContainEveryDateEnfournementSlice_AcFr9_5(string dtoPropertyName)
    {
        Assert.True(Kape22Mapper.IsIgnored(dtoPropertyName));
    }

    // Converts the reference Fichier, then overwrites the Header Date Champ text content.
    private static string NormalizedXmlWithHeaderDate(string value)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("header")!.Element("Date")!.Value = value;
        return document.ToString();
    }

    // Converts the reference Fichier, then removes the named Detail Champs entirely, mirroring how
    // Step 1 omits the element of a blank typed Champ (PRD D27).
    private static string NormalizedXmlWithoutDetailChamps(params string[] champIds)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        XElement message = document.Root!.Element("message")!;
        foreach (string champId in champIds)
        {
            message.Element(champId)?.Remove();
        }

        return document.ToString();
    }

    // A fixed winter instant (Paris UTC+1) for the tests that do not assert on DateReception.
    private static TimeProvider WinterClock() =>
        new FixedTimeProvider(DateTimeOffset.Parse("2026-02-10T08:00:00Z", CultureInfo.InvariantCulture));

    private static string ConvertReferenceFichier()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture(ReferenceFichierName), EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, "reference fixture failed to convert.");
        return conversion.Xml!;
    }

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    private static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));

    // Minimal TimeProvider stub: only GetUtcNow is consumed, the derived rules resolve the Paris zone
    // explicitly rather than through LocalTimeZone.
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;
    }
}
