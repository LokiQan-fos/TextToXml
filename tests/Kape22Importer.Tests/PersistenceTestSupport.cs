using System;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Shared scaffolding for the Story 2.8 persistence tests: convert the reference P60 Fichier, run it
// through Kape22Mapper, and mutate the normalized XML to produce success / rejection MapResults with a
// deterministic clock. Mirrors the helpers already inlined in DerivedFieldsTests / CoherenceWarningsTests
// (a fold-in of those copies is tracked in deferred-work.md).
internal static class PersistenceTestSupport
{
    // The reference Fichier and its own well-formed name (File_Emet_Recepteur_NumeroFichier).
    public const string ReferenceFichierName = "P60_847_682_001";

    // A fixed winter instant (Paris UTC+1). The persistence tests need a deterministic clock (AR-12).
    public static TimeProvider WinterClock() =>
        new FixedClock(DateTimeOffset.Parse("2026-02-10T08:00:00Z", CultureInfo.InvariantCulture));

    // A mapped, insertable entity from the untouched reference Fichier.
    public static MapResult<L_D_KAPE22> MapReferenceFichier() =>
        Kape22Mapper.Map(ConvertReferenceFichier(), ReferenceFichierName, WinterClock());

    // Converts the reference Fichier, applies a mutation to the normalized XML, then maps it.
    public static MapResult<L_D_KAPE22> MapMutatedFichier(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return Kape22Mapper.Map(document.ToString(), ReferenceFichierName, WinterClock());
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

    // A valid P60 reference Fichier from the TextToXml fixtures; its bytes are already Windows-1252.
    public static byte[] ReadValidFixture(string fichierName) =>
        File.ReadAllBytes(RepoLayout.ProjectFile($"tests/TextToXml.Tests/fixtures/valid/{fichierName}"));
}

// Minimal TimeProvider stub: only GetUtcNow is consumed.
internal sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
{
    private readonly DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => this.utcNow;
}
