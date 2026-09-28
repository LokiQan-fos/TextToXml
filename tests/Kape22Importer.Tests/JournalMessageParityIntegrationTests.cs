using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml.Tests;
using Xunit;
using static Kape22Importer.Tests.TestSupport;

namespace Kape22Importer.Tests;

// Story 6.1 (FR-24, AC-FR24-6): the L_D_LOG_COMMANDE Message values the P60 pipeline writes for the
// reference Fichiers - every rejection shape and the ten successes - are pinned as literals captured on
// the pre-migration baseline (89a75fb), so moving the journal onto IFichierJournal cannot change a single
// character. A characterization test: green on the baseline, then kept green (CC-1 exemption). The rows
// are read back with plain SQL so the read itself does not depend on either journal implementation.
// Integration category (AR-12), commit + reset regime.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class JournalMessageParityIntegrationTests(SqlServerIntegrationFixture fixture)
{
    // Detail-block Position/Size per Templates/P60.xml.
    private const int CodeConsignePitsPosition = 146;

    private const int CodeConsignePitsSize = 12;

    private const int CodeOpeChutagePosition = 241;

    private const int CodeOpeChutageSize = 3;

    private const int CodeOpeDecoupePosition = 284;

    private const int CodeOpeDecoupeSize = 3;

    private const int CodeOpePitsPosition = 125;

    private const int CodeOpePitsSize = 3;

    private const string InitiatingServer = "AFS017";

    private const int NombreLingotsFour1Position = 417;

    private const int NombreLingotsFour1Size = 2;

    private static readonly string[] TenFichiers =
    [
        "P60_847_682_001", "P60_847_682_002", "P60_847_682_003", "P60_847_682_004", "P60_847_682_005",
        "P60_847_682_006", "P60_847_682_007", "P60_847_682_008", "P60_847_682_009", "P60_847_682_010",
    ];

    // The baseline Messages, in write order: five rejections of the reference Fichier, then the ten
    // successes.
    private static readonly string[] ExpectedMessages =
    [
        "001 — REJETÉ : 1 erreur(s) : Le Champ obligatoire 'Client' est vide (colonne Client NOT NULL).",
        "001 — REJETÉ : 1 erreur(s) : OF '2039771' : la répartition des lingots aux fours (Four1=99 + Four2=0 = 99) ne correspond pas au nombre de demi-produits attendu (1).",
        "001 — REJETÉ : 1 erreur(s) : OF '2039771' : aucune consigne d'enfournement (L_D_SECTIONCHARGE_PITS) n'a été trouvée.",
        "001 — REJETÉ : OF '2039771' : la coulée '065718' est introuvable dans L_D_COULEE.",
        "001 — REJETÉ : OF '2039771' : plusieurs consignes partagent le même CodeOperation 'XC1'.",
        "001 — OK",
        "002 — OK",
        "003 — OK",
        "004 — OK",
        "005 — OK",
        "006 — OK",
        "007 — OK",
        "008 — OK",
        "009 — OK",
        "010 — OK",
    ];

    [SkippableFact]
    [Trait("AC", "FR24-6")]
    public void Import_ReferenceFichiers_WriteTheBaselineMessages_AcFr24_6()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        fixture.ResetData();

        foreach ((string name, byte[] content) in Fichiers())
        {
            Processor().Import(name, content);
        }

        Assert.Equal(ExpectedMessages, ReadMessages());
    }

    private static IEnumerable<(string Name, byte[] Content)> Fichiers()
    {
        byte[] reference = InsertableReferenceFichier();
        string codeOpeChutage = ReadDetailChamp(reference, CodeOpeChutagePosition, CodeOpeChutageSize);

        // Kape22Mapper rejection (RequiredFieldMissing), summarized as "<count> erreur(s) : ...".
        yield return (ReferenceFichierName, BlankClientReferenceFichier());

        // FR-20 rejections raised by Kape22ImportBundleMapper.
        yield return (ReferenceFichierName, WithDetailChamp(reference, NombreLingotsFour1Position, NombreLingotsFour1Size, "99"));
        yield return (ReferenceFichierName, WithDetailChamp(reference, CodeOpePitsPosition, CodeOpePitsSize, string.Empty));

        // Kape22Persister rejections: the missing cold Coulee and the accumulated business-rule message.
        yield return (ReferenceFichierName, WithDetailChamp(reference, CodeConsignePitsPosition, CodeConsignePitsSize, "1"));
        yield return (ReferenceFichierName, WithDetailChamp(reference, CodeOpeDecoupePosition, CodeOpeDecoupeSize, codeOpeChutage));

        foreach (string name in TenFichiers)
        {
            yield return (name, name == ReferenceFichierName ? reference : InsertableFichier(name));
        }
    }

    private Kape22FichierProcessor Processor() =>
        new(
            fixture.NewAscoLsiContext,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Import:Commande"] = "P60",
                    ["Import:InitiatingServer"] = InitiatingServer,
                })
                .Build(),
            fixture.NewJournal(),
            new ImportOptions { ArchiveFolder = "archive" },
            WinterClock(),
            NullLogger<Kape22FichierProcessor>.Instance);

    private string[] ReadMessages()
    {
        using SqlConnection connection = new(fixture.AscoLsiConnectionString);
        connection.Open();

        using SqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT [Message] FROM dbo.L_D_LOG_COMMANDE ORDER BY [Id];";

        using SqlDataReader reader = command.ExecuteReader();
        List<string> messages = [];
        while (reader.Read())
        {
            messages.Add(reader.GetString(0));
        }

        return [.. messages];
    }
}
