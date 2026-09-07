using System;
using System.Linq;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.5 (FR-8): StartupCompatibilityCheck.Verify inspects the built EF model and the embedded P60
// Descripteur, and refuses to start the worker when the Descripteur describes something L_D_KAPE22
// cannot accept. Written test-first (CC-1): every assertion is red until Verify ships. The tests read
// the built model and the embedded resource only, so they need no database and stay in the Unit
// category (AR-12).
[Trait("Category", TestCategory.Unit)]
public class StartupCompatibilityTests
{
    // AC-FR8-1: a mapped Champ whose datatype does not fit the entity property CLR type (here string
    // column Client forced to datatype="int") makes the worker refuse to start, and the offending
    // pair is named.
    [Fact]
    [Trait("AC", "FR8-1")]
    public void Verify_MappedChampDatatypeIncompatibleWithColumn_ThrowsListingThePair_AcFr8_1()
    {
        string descriptor = DescriptorWithMessageChange(message =>
            MessageValue(message, "Client").SetAttributeValue("datatype", "int"));

        StartupCompatibilityException error = Assert.Throws<StartupCompatibilityException>(
            () => StartupCompatibilityCheck.Verify(Model(), descriptor));

        Assert.Contains("Client", error.Message);
        Assert.Contains("datatype='int'", error.Message);
        Assert.Contains("String", error.Message);
    }

    // AC-FR8-1: independent incompatibilities are collected into one exception so the deployment can be
    // fixed in a single pass (here an incompatible datatype on Client and a removed source for Coulee).
    [Fact]
    [Trait("AC", "FR8-1")]
    public void Verify_SeveralIncompatibilities_AreAllListedInOneException_AcFr8_1()
    {
        string descriptor = DescriptorWithMessageChange(message =>
        {
            MessageValue(message, "Client").SetAttributeValue("datatype", "int");
            MessageValue(message, "Coulee").Remove();
        });

        StartupCompatibilityException error = Assert.Throws<StartupCompatibilityException>(
            () => StartupCompatibilityCheck.Verify(Model(), descriptor));

        Assert.Contains("Client", error.Message);
        Assert.Contains("Coulee", error.Message);
    }

    // AC-FR8-2: a mapped string Champ whose Size exceeds the column max_length (here Type, NCHAR(1),
    // forced to Size="50") makes the worker refuse to start, and the Size and max_length are named.
    [Fact]
    [Trait("AC", "FR8-2")]
    public void Verify_MappedStringChampSizeExceedsColumnMaxLength_ThrowsListingSizeAndMaxLength_AcFr8_2()
    {
        string descriptor = DescriptorWithMessageChange(message =>
            MessageValue(message, "Type").SetAttributeValue("Size", "50"));

        StartupCompatibilityException error = Assert.Throws<StartupCompatibilityException>(
            () => StartupCompatibilityCheck.Verify(Model(), descriptor));

        Assert.Contains("Type", error.Message);
        Assert.Contains("Size=50", error.Message);
        Assert.Contains("max_length=1", error.Message);
    }

    // AC-FR8-3: a NOT NULL column with no mapped Champ and no FR-9 derived rule (here Coulee, whose
    // <value> is removed from the message Bloc) makes the worker refuse to start.
    [Fact]
    [Trait("AC", "FR8-3")]
    public void Verify_NotNullColumnHasNoSource_Throws_AcFr8_3()
    {
        string descriptor = DescriptorWithMessageChange(message => MessageValue(message, "Coulee").Remove());

        StartupCompatibilityException error = Assert.Throws<StartupCompatibilityException>(
            () => StartupCompatibilityCheck.Verify(Model(), descriptor));

        Assert.Contains("Coulee", error.Message);
    }

    // AC-FR8-3: NumeroFichier and DateReception are NOT NULL columns fed by an FR-9 derived rule, not a
    // Detail Champ (PRD Annexe B). The nominal Descripteur must not flag them as sourceless.
    [Fact]
    [Trait("AC", "FR8-3")]
    public void Verify_DerivedNotNullColumnsAreTreatedAsSourced_AcFr8_3()
    {
        Assert.Equal(
            new[] { "DateReception", "NumeroFichier" },
            StartupCompatibilityCheck.DerivedRequiredColumns.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        Assert.Null(Record.Exception(() => StartupCompatibilityCheck.Verify(Model(), EmbeddedDescriptor.Xml)));
    }

    // AC-FR8-4: the nominal KAPE22 Descripteur and the real L_D_KAPE22 model are compatible, so Verify
    // returns without throwing and the worker starts.
    [Fact]
    [Trait("AC", "FR8-4")]
    public void Verify_NominalDescriptorAndTable_DoesNotThrow_AcFr8_4()
    {
        Assert.Null(Record.Exception(() => StartupCompatibilityCheck.Verify(Model(), EmbeddedDescriptor.Xml)));
    }

    // The <value> of the message Bloc carrying the given Id.
    private static XElement MessageValue(XElement message, string id) =>
        message.Elements("value").First(value => (string?)value.Attribute("Id") == id);

    // The embedded Descripteur with a mutation applied to its <message> Bloc, serialized back to a string.
    private static string DescriptorWithMessageChange(Action<XElement> mutate)
    {
        XDocument document = XDocument.Parse(EmbeddedDescriptor.Xml);
        mutate(document.Root!.Element("message")!);
        return document.ToString();
    }

    // The built L_D_KAPE22 EF model. Building the model opens no connection, so any syntactically valid
    // connection string works.
    private static IModel Model()
    {
        DbContextOptions<AscoLsiDbContext> options = new DbContextOptionsBuilder<AscoLsiDbContext>()
            .UseSqlServer("Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;")
            .Options;

        using AscoLsiDbContext context = new(options);
        return context.Model;
    }
}
