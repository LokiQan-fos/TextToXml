using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 2.4: Kape22Mapper.Map(normalizedXml, sourceFileName) turns the Kape22File DTO produced by
// P60Deserializer (Story 2.3) into an insertable L_D_KAPE22 entity per PRD Annexe B. Written test-first
// (CC-1): red until Kape22Mapper ships. Unit-only (AR-12), no database needed.
[Trait("Category", TestCategory.Unit)]
public class Kape22MapperTests
{
    // AC-FR7-2: every homonymous Detail property lands on the entity with the DTO's typed value.
    // Spot-checks OF/Client/Coulee/Nuance/Type/Indice directly against the normalized XML (ground
    // truth, independent of Kape22Mapper's own reflection), then sweeps every other mapped property.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_ValidFichier_CopiesEveryHomonymousDetailProperty_AcFr7_2()
    {
        string normalizedXml = ConvertReferenceFichier();
        MapResult<L_D_KAPE22> result = Map(normalizedXml, ReferenceFichierName);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        L_D_KAPE22 entity = result.Value!;

        XElement message = XDocument.Parse(normalizedXml).Root!.Element("message")!;
        Assert.Equal((string)message.Element("OF")!, entity.OF);
        Assert.Equal((string)message.Element("Client")!, entity.Client);
        Assert.Equal((string)message.Element("Coulee")!, entity.Coulee);
        Assert.Equal((string)message.Element("Nuance")!, entity.Nuance);
        Assert.Equal((string)message.Element("Type")!, entity.Type);
        Assert.Equal(int.Parse((string)message.Element("Indice")!, CultureInfo.InvariantCulture), entity.Indice);

        Kape22FileMessage dtoMessage = P60Deserializer.Deserialize(normalizedXml).File!.Message;
        foreach (PropertyInfo source in typeof(Kape22FileMessage).GetProperties())
        {
            if (Kape22Mapper.IsIgnored(source.Name))
            {
                continue;
            }

            string targetName = Kape22Mapper.ResolveTargetName(source.Name);

            // A blank Champ on one of these columns is a legacy default, not a verbatim copy (Annexe B
            // "Legacy blank-Champ defaults"), so it is not a homonymous-copy assertion. The blank int
            // rule stays in the sweep: DefaultForNonNullable already yields the value it expects.
            if (Kape22Mapper.LegacyBlankFillColumns.Contains(targetName))
            {
                continue;
            }

            PropertyInfo target = typeof(L_D_KAPE22).GetProperty(
                targetName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

            object? expected = source.GetValue(dtoMessage) ?? Kape22Mapper.DefaultForNonNullable(target.PropertyType);
            object? actual = target.GetValue(entity);
            Assert.True(
                Equals(expected, actual),
                $"{source.Name} -> {target.Name}: expected {expected ?? "null"}, got {actual ?? "null"}.");
        }
    }

    // AC-FR7-4: the one Annexe B naming exception (DTO OFOriginInterne -> entity OForiginInterne) is
    // applied. Exercised with a non-blank value: a blank OForiginInterne Champ now maps to NULL as a
    // legacy default (AC-FR7-2), which would not prove the rename.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void Map_OFOriginInterneChamp_LandsOnEntityOForiginInterne_AcFr7_4()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("message")!.Element("OForiginInterne")!.Value = "X";

        MapResult<L_D_KAPE22> result = Map(document.ToString(), ReferenceFichierName);

        Assert.True(result.Success);
        Assert.Equal("X", result.Value!.OForiginInterne);
    }

    // AC-FR7-2 (Annexe B "Legacy blank-Champ defaults"): a blank typed integer Champ maps to 0, not
    // NULL. The legacy import zero-filled every blank int Champ (production parity 2026-09-07: no NULL
    // in any L_D_KAPE22 int column over 17710 rows). The reference Fichier leaves both blank.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_BlankTypedIntChamp_MapsToZeroNotNull_AcFr7_2()
    {
        L_D_KAPE22 entity = Map(ConvertReferenceFichier(), ReferenceFichierName).Value!;

        Assert.Equal(0, entity.MatriculeClient);
        Assert.Equal(0, entity.ChutagePied);
    }

    // AC-FR7-2: a typed integer Champ carrying a real value is copied unchanged (reference Fichier
    // DiametreProduit = 6250).
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_PopulatedTypedIntChamp_IsCopiedUnchanged_AcFr7_2()
    {
        L_D_KAPE22 entity = Map(ConvertReferenceFichier(), ReferenceFichierName).Value!;

        Assert.Equal(6250, entity.DiametreProduit);
    }

    // AC-FR7-2: a present int element whose value is 0 stays 0 - a real 0 is not the blank fill.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_TypedIntChampValueZero_IsNotTreatedAsBlank_AcFr7_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("message")!.Element("Epaisseur")!.Value = "0";

        L_D_KAPE22 entity = Map(document.ToString(), ReferenceFichierName).Value!;

        Assert.Equal(0, entity.Epaisseur);
    }

    // AC-FR7-2: OForiginInterne is the one string column the legacy import leaves NULL for a blank
    // Champ (production parity: NULL in 100% of 17710 rows). The reference Fichier leaves it blank.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_BlankOForiginInterneChamp_MapsToNull_AcFr7_2()
    {
        L_D_KAPE22 entity = Map(ConvertReferenceFichier(), ReferenceFichierName).Value!;

        Assert.Null(entity.OForiginInterne);
    }

    // AC-FR7-2: a populated OForiginInterne Champ is copied verbatim. assumed, unverified - the legacy
    // import source is unavailable and no sample carries a populated value (see deferred-work.md).
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_PopulatedOForiginInterneChamp_IsCopiedVerbatim_AcFr7_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("message")!.Element("OForiginInterne")!.Value = "X";

        L_D_KAPE22 entity = Map(document.ToString(), ReferenceFichierName).Value!;

        Assert.Equal("X", entity.OForiginInterne);
    }

    // AC-FR7-2: a blank AcompteSolde Champ maps to 'S' - the legacy default ('S' in 100% of 17710
    // production rows). The reference Fichier leaves it blank.
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_BlankAcompteSoldeChamp_MapsToS_AcFr7_2()
    {
        L_D_KAPE22 entity = Map(ConvertReferenceFichier(), ReferenceFichierName).Value!;

        Assert.Equal("S", entity.AcompteSolde);
    }

    // AC-FR7-2: a populated AcompteSolde Champ is copied unchanged. assumed, unverified - the legacy
    // import source is unavailable and no sample carries a populated value (see deferred-work.md).
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_PopulatedAcompteSoldeChamp_IsCopiedUnchanged_AcFr7_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("message")!.Element("AcompteSolde")!.Value = "A";

        L_D_KAPE22 entity = Map(document.ToString(), ReferenceFichierName).Value!;

        Assert.Equal("A", entity.AcompteSolde);
    }

    // AC-FR7-2: the NULL fill is specific to OForiginInterne - every other blank string Champ still
    // maps to an empty string (AC-FR5-6 emits the element, the default copy keeps it).
    [Fact]
    [Trait("AC", "FR7-2")]
    public void Map_OtherBlankStringChamp_StaysEmptyString_AcFr7_2()
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        document.Root!.Element("message")!.Element("MarqueCommerciale")!.Value = string.Empty;

        L_D_KAPE22 entity = Map(document.ToString(), ReferenceFichierName).Value!;

        Assert.Equal(string.Empty, entity.MarqueCommerciale);
    }

    // AC-FR7-4: proves ResolveTargetName actually substitutes the mapped name (case-sensitive check),
    // independent of Kape22FileMessage/L_D_KAPE22's real property casing, which happens to already
    // match case-insensitively and so cannot on its own prove the exception mechanism is exercised.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void ResolveTargetName_MappedSourceName_SubstitutesDictionaryTarget_AcFr7_4()
    {
        Assert.Equal("OForiginInterne", Kape22Mapper.ResolveTargetName("OFOriginInterne"));
        Assert.NotEqual("OFOriginInterne", Kape22Mapper.ResolveTargetName("OFOriginInterne"), StringComparer.Ordinal);
    }

    // AC-FR7-4: an unmapped source name falls back to itself unchanged.
    [Fact]
    [Trait("AC", "FR7-4")]
    public void ResolveTargetName_UnmappedSourceName_ReturnsInputUnchanged_AcFr7_4()
    {
        Assert.Equal("SomeUnmappedProperty", Kape22Mapper.ResolveTargetName("SomeUnmappedProperty"));
    }

    // AC-FR7-5: Annexe B's ignored Detail properties are not copied anywhere, and Map raises no error.
    [Theory]
    [InlineData("Element")]
    [InlineData("KAP")]
    [InlineData("Segment")]
    [InlineData("Date")]
    [InlineData("Reserve")]
    [InlineData("ReserveSVT")]
    [InlineData("ReserveRefroidissoir")]
    [Trait("AC", "FR7-5")]
    public void Map_IgnoredDetailProperty_IsInTheIgnoredSet_AcFr7_5(string dtoPropertyName)
    {
        Assert.True(Kape22Mapper.IsIgnored(dtoPropertyName));
    }

    [Fact]
    [Trait("AC", "FR7-5")]
    public void Map_ValidFichier_RaisesNoErrorDespiteIgnoredProperties_AcFr7_5()
    {
        MapResult<L_D_KAPE22> result = Map(ConvertReferenceFichier(), ReferenceFichierName);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    // AC-FR7-6: each Annexe B naming-exception entry names a source member that exists on
    // Kape22FileMessage and a target member that exists on L_D_KAPE22 (both by reflection).
    public static TheoryData<string, string> NamingExceptionEntries()
    {
        TheoryData<string, string> data = new();
        foreach ((string source, string target) in Kape22Mapper.NamingExceptions)
        {
            data.Add(source, target);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NamingExceptionEntries))]
    [Trait("AC", "FR7-6")]
    public void Map_NamingExceptionEntry_SourceAndTargetMembersExist_AcFr7_6(string sourceName, string targetName)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        Assert.True(
            typeof(Kape22FileMessage).GetProperty(sourceName, Flags) is not null,
            $"Kape22FileMessage has no property named '{sourceName}'.");
        Assert.True(
            typeof(L_D_KAPE22).GetProperty(targetName, Flags) is not null,
            $"L_D_KAPE22 has no property named '{targetName}'.");
    }

    // Edge case: a blank Indice (element omitted from the normalized XML) does not throw. Story 2.6
    // (AC-FR9-4) turned this into a RequiredFieldMissing rejection - see DerivedFieldsTests; here we
    // only pin that Map stays exception-free on the blank-Indice path.
    [Fact]
    public void Map_BlankIndice_DoesNotThrow()
    {
        string withoutIndice = Regex.Replace(ConvertReferenceFichier(), "<Indice>[^<]*</Indice>", string.Empty);

        Exception? exception = Record.Exception(() => Map(withoutIndice, ReferenceFichierName));

        Assert.Null(exception);
    }

    // Map's deserialization-failure passthrough: when P60Deserializer.Deserialize rejects the normalized
    // XML (schema violation, deserialized.File is null), Map returns early with the same Errors and no
    // Value, instead of throwing or mapping a partial entity.
    [Fact]
    public void Map_NonConformantNormalizedXml_ReturnsDeserializerErrorsAndNoValue()
    {
        MapResult<L_D_KAPE22> result = Map(NonConformantNormalizedXml(), ReferenceFichierName);

        Assert.False(result.Success);
        Assert.Null(result.Value);
        ConversionError error = Assert.Single(result.Errors);
        Assert.Equal(Block.File, error.Block);
        Assert.Equal(ErrorCode.PersistenceError, error.Code);
    }
}
