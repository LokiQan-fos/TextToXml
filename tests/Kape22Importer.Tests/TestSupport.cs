using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Shared scaffolding for the Kape22Importer.Tests suite (Epic 3 story-0 hygiene, retro action A-1): the
// reference P60 Fichier and its name, a deterministic clock, the embedded P60 Descripteur / schema
// readers, and the convert + map + mutate helpers the mapper and persistence tests all need. Import
// per file with `using static Kape22Importer.Tests.TestSupport;`.
internal static class TestSupport
{
    // The reference Fichier and its own well-formed name (File_Emet_Recepteur_NumeroFichier).
    public const string ReferenceFichierName = "P60_847_682_001";

    // The reference Fichier's Client Detail Champ. Blanking it (BlankClientReferenceFichier) forces a
    // Step 2 rejection (RequiredFieldMissing) while the Fichier still deserializes, so NumeroFichier and
    // OF stay readable and Kape22Persister writes the REJETÉ log row.
    public const string ReferenceClient = "APERAM ALLOYS";

    // The LogicalName Kape22Importer.csproj pins for the embedded P60 Descripteur and schema. The
    // Descripteur has a production accessor (EmbeddedDescriptor.Xml); the schema does not.
    public const string EmbeddedP60XmlResourceName = "Kape22Importer.Templates.P60.xml";

    public const string EmbeddedP60XsdResourceName = "Kape22Importer.Templates.P60.xsd";

    // A fixed winter instant (Paris UTC+1) for the tests that need a deterministic clock (AR-12) but
    // do not assert on a timestamp value.
    public static TimeProvider WinterClock() =>
        new FixedClock(DateTimeOffset.Parse("2026-02-10T08:00:00Z", CultureInfo.InvariantCulture));

    // Kape22Mapper.Map on the fixed WinterClock, the clock most mapper tests want.
    public static MapResult<L_D_KAPE22> Map(string normalizedXml, string sourceFileName) =>
        new Kape22Mapper(WinterClock()).Map(normalizedXml, sourceFileName);

    // A mapped, insertable entity from the untouched reference Fichier.
    public static MapResult<L_D_KAPE22> MapReferenceFichier() =>
        Map(ConvertReferenceFichier(), ReferenceFichierName);

    // Converts the reference Fichier, applies a mutation to the normalized XML, then maps it.
    public static MapResult<L_D_KAPE22> MapMutatedFichier(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return Map(document.ToString(), ReferenceFichierName);
    }

    // Sets the text of a Champ element in the named Bloc. Blanking a NOT NULL string Champ (Client, ...)
    // makes Kape22Mapper reject the Fichier with RequiredFieldMissing while it still deserializes, so
    // NumeroFichier and OF stay readable.
    public static void SetChamp(XDocument document, string bloc, string champId, string value) =>
        document.Root!.Element(bloc)!.Element(champId)!.Value = value;

    public static string ConvertReferenceFichier()
    {
        ConversionResult conversion = Converter.Convert(ReadValidFixture(ReferenceFichierName), EmbeddedDescriptor.Xml);
        Assert.True(conversion.Success, "reference fixture failed to convert.");
        return conversion.Xml!;
    }

    // The normalized XML of the reference Fichier with an element P60.xsd does not declare inserted as
    // the first child of <message>, so it fails schema validation.
    public static string NonConformantNormalizedXml() =>
        ConvertReferenceFichier().Replace("<message>", "<message><Bogus>x</Bogus>", StringComparison.Ordinal);

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    public static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));

    // The reference Fichier's raw bytes with its Client Champ blanked in place, so Converter.Convert
    // still succeeds but Kape22Mapper.Map rejects the Fichier with RequiredFieldMissing.
    public static byte[] BlankClientReferenceFichier() =>
        WithText(ReadValidFixture(ReferenceFichierName), ReferenceClient, new string(' ', ReferenceClient.Length));

    // The bytes of source with every occurrence of find replaced by replacement. Latin-1 round-trips
    // every byte 1:1, so an ASCII substring swap keeps a fixed-width Fichier's layout intact.
    public static byte[] WithText(byte[] source, string find, string replacement) =>
        Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(source).Replace(find, replacement));

    // The embedded P60 schema (Templates/P60.xsd), read the same way P60Deserializer reads it.
    public static string EmbeddedP60Xsd() => EmbeddedResource(EmbeddedP60XsdResourceName);

    // One embedded resource of the Kape22Importer assembly, read exactly as the importer reads it at
    // runtime.
    private static string EmbeddedResource(string logicalName)
    {
        Assembly importer = typeof(EmbeddedDescriptor).Assembly;

        using Stream stream = importer.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{logicalName}' is missing from {importer.GetName().Name}.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}

// Minimal TimeProvider stub: only GetUtcNow is consumed.
internal sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
{
    private readonly DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => this.utcNow;
}

// Hands out a fresh AscoLsiDbContext on every call, all bound to one EF in-memory database so a write
// from an earlier Import is visible to a later one, and records every context handed out (Story 3.2,
// AC-FR13-4). Reused by the Story 3.3 double-logging unit tests.
internal sealed class InMemoryContextFactory
{
    private readonly string databaseName = Guid.NewGuid().ToString();

    public List<AscoLsiDbContext> Handed { get; } = [];

    public AscoLsiDbContext Next()
    {
        AscoLsiDbContext context = new(Build());
        this.Handed.Add(context);
        return context;
    }

    public AscoLsiDbContext Reader() => new(Build());

    private DbContextOptions<AscoLsiDbContext> Build() =>
        new DbContextOptionsBuilder<AscoLsiDbContext>().UseInMemoryDatabase(this.databaseName).Options;
}
