using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using AscoLsiJournal;
using FichierJournal;
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

    // The reference Fichier's mapped L_D_KAPE22 entity, the standard input for the Story 4.3 downstream
    // mappers (OrdreFabricationMapper, CouleeMapper), which take an entity rather than raw XML.
    public static L_D_KAPE22 ReferenceKape22() => MapReferenceFichier().Value!;

    // Converts the reference Fichier, applies a mutation to the normalized XML, then maps it.
    public static MapResult<L_D_KAPE22> MapMutatedFichier(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return Map(document.ToString(), ReferenceFichierName);
    }

    // Story 4.6: a mapped, insertable Kape22ImportBundle from the reference Fichier, mirroring
    // MapReferenceFichier(). Coulee is set to the conventional internal value "065718" used throughout
    // this assembly's hand-built fixtures. Story 6.9: the reference Fichier is cold (its CodeConsignePits
    // "1 205 00 999" starts with "1"), so it persists only once its Coulee is in L_D_COULEE - see
    // SeedCoulees.
    public static Kape22ImportBundle MapReferenceBundle() =>
        MapMutatedBundle(document => SetChamp(document, "message", "Coulee", "065718"));

    // Converts the reference Fichier, applies a mutation to the normalized XML, then maps it through
    // Kape22ImportBundleMapper (Story 4.6).
    public static Kape22ImportBundle MapMutatedBundle(Action<XDocument> mutate)
    {
        XDocument document = XDocument.Parse(ConvertReferenceFichier());
        mutate(document);
        return new Kape22ImportBundleMapper(WinterClock()).Map(document.ToString(), ReferenceFichierName);
    }

    // Converts a raw Fichier and maps it through Kape22ImportBundleMapper, the way Kape22FichierProcessor
    // does.
    public static Kape22ImportBundle MapFichier(byte[] content, string fichierName)
    {
        ConversionResult conversion = Converter.Convert(content, EmbeddedDescriptor.Xml);
        Assert.True(
            conversion.Success && conversion.Xml is not null,
            $"{fichierName} failed to convert: {string.Join("; ", conversion.Errors.Select(error => error.Message))}");
        return new Kape22ImportBundleMapper(WinterClock()).Map(conversion.Xml!, fichierName);
    }

    // Story 6.9 (AC-FR20-5): every sample Fichier (001..010) is cold - its CodeConsignePits starts with
    // "1" - so it imports only once its Coulee is in L_D_COULEE, the state production is in when a P60
    // names a cold Coulee. Adds the Coulee row each named sample maps to (the reference one through
    // InsertableReferenceFichier, hence "065718"), skipping a Coulee already on file.
    public static void SeedCoulees(Func<AscoLsiDbContext> newContext, params string[] fichierNames)
    {
        foreach (string fichierName in fichierNames)
        {
            byte[] content = fichierName == ReferenceFichierName ? InsertableReferenceFichier() : InsertableFichier(fichierName);
            SeedCoulee(newContext, MapFichier(content, fichierName));
        }
    }

    // Story 6.9: L_D_COULEE holds exactly the reference Coulee SeedCoulees put there, unmodified - the
    // rejected or rolled-back import under test added and changed nothing.
    public static void AssertOnlyTheSeededReferenceCoulee(IQueryable<L_D_COULEE> rows)
    {
        L_D_COULEE seeded = MapFichier(InsertableReferenceFichier(), ReferenceFichierName).Coulee!;
        L_D_COULEE row = Assert.Single(rows.AsNoTracking());
        Assert.Equal(seeded.IdCoulee.Trim(), row.IdCoulee.Trim());
        Assert.Equal(seeded.Nuance.Trim(), row.Nuance.Trim());
        Assert.Equal(seeded.DateReception, row.DateReception);
        Assert.Equal(seeded.DerniereModif, row.DerniereModif);
        Assert.Equal(seeded.EtatReception, row.EtatReception);
        Assert.Equal(seeded.Externe, row.Externe);
        Assert.Equal(seeded.NbLingotRestantARefroidir, row.NbLingotRestantARefroidir);
    }

    // Story 6.9: adds the bundle's own Coulee row to L_D_COULEE unless it is already on file. The bundle
    // must have mapped cleanly, since a failed mapping carries no Coulee.
    public static void SeedCoulee(Func<AscoLsiDbContext> newContext, Kape22ImportBundle bundle)
    {
        Assert.True(
            bundle.Success,
            $"{bundle.NumeroFichier} failed to map: {string.Join("; ", bundle.Errors.Select(error => error.Message))}");
        L_D_COULEE coulee = bundle.Coulee
            ?? throw new InvalidOperationException($"{bundle.NumeroFichier} mapped without a Coulee to seed.");

        using AscoLsiDbContext context = newContext();
        if (!context.CouleeRows.Any(row => row.IdCoulee == coulee.IdCoulee))
        {
            context.CouleeRows.Add(coulee);
            context.SaveChanges();
        }
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

    // Story 4.6: the reference Fichier's raw bytes with Coulee set to the conventional internal value
    // "065718" (see MapReferenceBundle), the byte-level counterpart for tests that run the whole
    // Kape22FichierProcessor.Import pipeline from raw bytes rather than from pre-parsed XML. Story
    // 4.3-bis: no longer zeroes the dimension/tolerance Champs - the mappers now scale them correctly
    // (DecimalScale.Apply), so the raw fixture values are insertable as-is.
    public static byte[] InsertableReferenceFichier() =>
        WithText(ReadValidFixture(ReferenceFichierName), "165718", "065718");

    // Story 4.6: any of the ten P60/ reference samples, ready to insert (see InsertableReferenceFichier);
    // every sample but the reference one already has a valid hot-Coulee format, so no Coulee correction is
    // needed here.
    public static byte[] InsertableFichier(string fichierName) => ReadValidFixture(fichierName);

    // Blanks a fixed-width Detail-block Champ in place by its Templates/P60.xml Position/Size; a blank
    // int Champ is zero-filled by Kape22Mapper.DefaultForNonNullable (D27), so this has the same effect
    // on the mapped entity as SetChamp(document, "message", champ, "0") on the post-Converter XDocument.
    public static byte[] WithDetailChamp(byte[] source, int position, int size, string rawValue)
    {
        string[] lines = Encoding.Latin1.GetString(source).Split("\r\n");
        lines[1] = lines[1][..position] + rawValue.PadRight(size) + lines[1][(position + size)..];
        return Encoding.Latin1.GetBytes(string.Join("\r\n", lines));
    }

    // Code-review patch (Épic 4 retro #3, story 4.11): the read-side counterpart of WithDetailChamp -
    // reads a fixed-width Detail-block Champ back out of a raw Fichier's bytes by its own
    // Templates/P60.xml Position/Size, so a test can force one field onto another field's own current
    // value instead of a hardcoded literal that would go stale silently if the fixture's value ever
    // changed.
    public static string ReadDetailChamp(byte[] source, int position, int size)
    {
        string[] lines = Encoding.Latin1.GetString(source).Split("\r\n");
        if (lines.Length < 2)
        {
            throw new ArgumentException("Fichier has no Detail block (fewer than 2 lines).", nameof(source));
        }

        return lines[1].Substring(position, size);
    }

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
// AC-FR13-4). Reused by the Story 3.3 double-logging unit tests. Story 6.1: Journal is the real LSI
// journal the host injects, over its own in-memory database, and LogRows reads back what it wrote.
internal sealed class InMemoryContextFactory
{
    private readonly string databaseName = Guid.NewGuid().ToString();

    private readonly string journalDatabaseName = Guid.NewGuid().ToString();

    // Story 6.9: the reference Fichier's cold Coulee is on file from the start (see
    // TestSupport.SeedCoulees), through Reader so it is not counted in Handed.
    public InMemoryContextFactory()
    {
        this.Journal = new AscoLsiFichierJournal(this.JournalContext, SqlServerIntegrationFixture.JournalInitiatingServer);
        TestSupport.SeedCoulees(this.Reader, TestSupport.ReferenceFichierName);
    }

    public List<AscoLsiDbContext> Handed { get; } = [];

    public AscoLsiFichierJournal Journal { get; }

    public L_D_LOG_COMMANDE[] LogRows()
    {
        using AscoLsiJournalDbContext context = this.JournalContext();
        return [.. context.LogCommandeRows.AsNoTracking().OrderBy(row => row.Id)];
    }

    public AscoLsiDbContext Next()
    {
        AscoLsiDbContext context = new(Build());
        this.Handed.Add(context);
        return context;
    }

    public AscoLsiDbContext Reader() => new(Build());

    private DbContextOptions<AscoLsiDbContext> Build() =>
        new DbContextOptionsBuilder<AscoLsiDbContext>().UseInMemoryDatabase(this.databaseName).Options;

    private AscoLsiJournalDbContext JournalContext() =>
        new(new DbContextOptionsBuilder<AscoLsiJournalDbContext>().UseInMemoryDatabase(this.journalDatabaseName).Options);
}

// Story 6.1: an IFichierJournal whose writes fail the way an unreachable LSI database does, after
// keeping the entries it was handed; HasSuccess fails only when failReads is set, and otherwise answers
// false.
internal sealed class FailingJournal(Exception failure, bool failReads = false) : IFichierJournal
{
    public List<FichierJournalEntry> Attempted { get; } = [];

    public bool HasSuccess(FichierJournalEntry entry) => failReads ? throw failure : false;

    public void Record(FichierJournalEntry entry)
    {
        this.Attempted.Add(entry);
        throw failure;
    }
}
