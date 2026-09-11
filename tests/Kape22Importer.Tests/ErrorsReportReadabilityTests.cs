using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 3.6 / SM-3: a deliberately corrupt Fichier must leave behind a <nom>.errors.json an operator
// reads without help - every distinct cause spelled out with its Bloc, Ligne, Champ, colonne, code,
// message and faulty value, codes written by name rather than as an ordinal. The two Annexe A.4
// corruptions are exercised: a non-numeric DiametreProduit (rejected at Step 1, InvalidInteger) and a
// blank NOT NULL Coulee (rejected at Step 2, RequiredFieldMissing), plus a two-cause variant proving
// the report never collapses distinct causes. The pipeline runs over EF InMemory (no SQL Server, no
// disk), so the measured behaviour is the report content, not persistence. Written test-first (CC-1).
// Vocabulary follows the PRD glossary (CC-5).
[Trait("Category", TestCategory.Unit)]
public class ErrorsReportReadabilityTests
{
    private const string InitiatingServer = "AFS017";

    // The inbox root folder key of the in-memory IFileSource.
    private const string InboxRoot = "";

    // Detail Champ slices from Templates/P60.xml, zero-based Position and Size.
    private const int CouleePosition = 50;

    private const int CouleeSize = 6;

    private const int DiametreProduitPosition = 59;

    private const int DiametreProduitSize = 4;

    private const int NuancePosition = 43;

    private const int NuanceSize = 7;

    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-09-08T10:00:00Z", CultureInfo.InvariantCulture);

    private static ImportOptions Options() => new()
    {
        ArchiveFolder = "archive",
        ErrorFolder = "error",
        InboxPath = "inbox",
        InitiatingServer = InitiatingServer,
        PollingInterval = TimeSpan.FromSeconds(30),
        ProcessingFolder = "processing",
        RetentionDays = 30,
    };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
                ["Import:InitiatingServer"] = InitiatingServer,
            })
            .Build();

    private static InboxScanner Scanner(InMemoryFileSource source)
    {
        Kape22FichierProcessor processor = new(
            new InMemoryContextFactory().Next,
            Configuration(),
            Options(),
            new FixedClock(Now),
            NullLogger<Kape22FichierProcessor>.Instance);

        return new InboxScanner(source, processor, Options(), new FixedClock(Now), new RecordingLogger<InboxScanner>());
    }

    // SM-3: a non-numeric DiametreProduit is rejected at Step 1; the Fichier lands in error/ and its
    // .errors.json carries one readable cause - Detail Bloc, Ligne 2, the Champ name, the InvalidInteger
    // code by name, the faulty raw value and a French message.
    [Fact]
    [Trait("AC", "SM-3")]
    public void Process_NonNumericDiametre_WritesAReadableSingleCauseReport_Sm3()
    {
        const string fichierName = "non_numeric_diametre";
        InMemoryFileSource source = new();
        source.Add(InboxRoot, fichierName, WithDetailChamp(DiametreProduitPosition, DiametreProduitSize, "11A0"), Now);

        Scanner(source).RunTick();

        Assert.True(source.Exists(Options().ErrorFolder, fichierName), "the corrupt Fichier must be quarantined to error/.");
        JsonElement cause = Assert.Single(ReadReport(source, fichierName).EnumerateArray());

        Assert.Equal("Detail", cause.GetProperty("Block").GetString());
        Assert.Equal(2, cause.GetProperty("LineNumber").GetInt32());
        Assert.Equal("DiametreProduit", cause.GetProperty("FieldId").GetString());
        Assert.Equal(nameof(ErrorCode.InvalidInteger), cause.GetProperty("Code").GetString());
        Assert.Equal("11A0", cause.GetProperty("RawValue").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cause.GetProperty("Message").GetString()));
    }

    // SM-3: a blank NOT NULL Coulee passes Step 1 and is rejected by the mapper; the error/ report names
    // the RequiredFieldMissing cause with its Champ and its L_D_KAPE22 column.
    [Fact]
    [Trait("AC", "SM-3")]
    public void Process_BlankRequiredCoulee_WritesAReadableSingleCauseReport_Sm3()
    {
        const string fichierName = "empty_required";
        InMemoryFileSource source = new();
        source.Add(InboxRoot, fichierName, WithDetailChamp(CouleePosition, CouleeSize, string.Empty), Now);

        Scanner(source).RunTick();

        Assert.True(source.Exists(Options().ErrorFolder, fichierName), "the corrupt Fichier must be quarantined to error/.");
        JsonElement cause = Assert.Single(ReadReport(source, fichierName).EnumerateArray());

        Assert.Equal("Detail", cause.GetProperty("Block").GetString());
        Assert.Equal(nameof(ErrorCode.RequiredFieldMissing), cause.GetProperty("Code").GetString());
        Assert.Equal("Coulee", cause.GetProperty("FieldId").GetString());
        Assert.Equal("Coulee", cause.GetProperty("Column").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cause.GetProperty("Message").GetString()));
    }

    // SM-3 (SM-C1): two blank NOT NULL Champs produce two distinct causes in the report - the harness
    // never reduces the count "to look clean", and the JSON is indented with codes written by name so an
    // operator reads it unaided.
    [Fact]
    [Trait("AC", "SM-3")]
    public void Process_TwoBlankRequiredChamps_ReportKeepsEveryDistinctCause_Sm3()
    {
        const string fichierName = "empty_required_pair";
        byte[] content = WithDetailChamp(CouleePosition, CouleeSize, string.Empty);
        content = WithDetailChamp(content, NuancePosition, NuanceSize, string.Empty);
        InMemoryFileSource source = new();
        source.Add(InboxRoot, fichierName, content, Now);

        Scanner(source).RunTick();

        byte[] reportBytes = source.Content(Options().ErrorFolder, fichierName + ".errors.json");
        string reportText = Encoding.UTF8.GetString(reportBytes);
        JsonElement report = JsonDocument.Parse(reportBytes).RootElement;

        List<string?> columns = report.EnumerateArray()
            .Select(cause => cause.GetProperty("Column").GetString())
            .ToList();
        Assert.Equal(2, columns.Count);
        Assert.Contains("Coulee", columns);
        Assert.Contains("Nuance", columns);
        Assert.All(
            report.EnumerateArray(),
            cause => Assert.Equal(nameof(ErrorCode.RequiredFieldMissing), cause.GetProperty("Code").GetString()));

        // Readable without help: the code is a name, not a number, and the document is indented.
        Assert.Contains(nameof(ErrorCode.RequiredFieldMissing), reportText, StringComparison.Ordinal);
        Assert.Contains('\n', reportText);
    }

    private static JsonElement ReadReport(InMemoryFileSource source, string fichierName)
    {
        byte[] bytes = source.Content(Options().ErrorFolder, fichierName + ".errors.json");
        return JsonDocument.Parse(bytes).RootElement;
    }

    // The reference Fichier bytes with the fixed-width slice a Detail Champ occupies overwritten in
    // place, keeping every other byte and the CRLF layout intact. A value shorter than the slot is
    // right-padded with spaces, matching how a real SAP export would leave a blank Champ.
    private static byte[] WithDetailChamp(int position, int size, string rawValue) =>
        WithDetailChamp(ReadValidFixture(ReferenceFichierName), position, size, rawValue);

    private static byte[] WithDetailChamp(byte[] reference, int position, int size, string rawValue)
    {
        string[] lines = Encoding.Latin1.GetString(reference).Split("\r\n");
        string detail = lines[1];
        string slot = rawValue.Length >= size ? rawValue[..size] : rawValue.PadRight(size);
        lines[1] = detail[..position] + slot + detail[(position + size)..];
        return Encoding.Latin1.GetBytes(string.Join("\r\n", lines));
    }
}
