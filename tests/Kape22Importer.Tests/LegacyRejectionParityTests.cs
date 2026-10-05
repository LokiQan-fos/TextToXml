using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TextToXml;
using TextToXml.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Kape22Importer.Tests;

// Remove at the switchover to the new worker: the legacy chain then stops writing the trace this test
// replays.
//
// Story 6.10: parity of rejection causes with the legacy chain. Every Fichier in P60/error/ was rejected
// by the legacy import in production. For each one, the legacy processing trace is read from production
// L_D_LOG_COMMANDE (SELECT only, ApplicationIntent=ReadOnly, CC-7), the state the Fichier met is rebuilt
// in AscoLSI_Test, the Fichier goes through the real Kape22FichierProcessor, and the cause obtained must
// be the one the legacy reason names. File names recycle, so only the latest legacy run counts.
//
// State rebuilt (spec Design Notes): the Fichier's Coulee row copied from production when it was received
// before the run; the Fichier's own mapped L_D_ORDRE_FABRICATION row when a GPAO "Création d'un OF" line
// for its OF precedes the run, in state ENC when an ENC line for it also precedes the run, else GPAO; the
// L_P_CONSIGNES_* tables copied whole. ponytail: only GPAO/ENC are rebuilt, not later states nor the 3
// D34 precondition tables - enough for the reference cases; add them when a case needs them.
//
// Opt-in like Kape22ProductionDataParityTests: needs the test instance AND
// ConnectionStrings:AscoLSI_Production, otherwise skips; no legacy trace for the Fichier also skips.
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class LegacyRejectionParityTests(SqlServerIntegrationFixture fixture, ITestOutputHelper output)
{
    // The hot-Coulee-not-starting-with-0 rejection the legacy applied: AC-FR20-3 is withdrawn, so the new
    // pipeline accepts it, a known gap.
    private const string Accepted = "acceptée";

    private static readonly Regex FichierName = new(@"^P60_\d+_\d+_\d+$", RegexOptions.Compiled);

    // Legacy reason fragment -> expected cause, matched in this order: the specific reasons first (in
    // alphabetical order), the generic "nombre d'OF sauvés : 0" summary last, so a run that logs both a
    // specific reason and the summary keeps the specific cause (review P-3).
    private static readonly (string Fragment, string Cause)[] LegacyReasons =
    [
        ("coulée froide", "AC-FR20-5"),
        ("different de la somme des lingots", "AC-FR20-2"),
        ("ne commance pas par le caractère '0'", Accepted),
        ("pas de consignes pour la répartition", "AC-FR20-2"),
        ("sans consignes d'enfournement", "AC-FR20-4"),
        ("nombre d'OF sauvés : 0", "AC-FR20-6"),
    ];

    // Rejection message fragment of the new pipeline -> obtained cause.
    private static readonly (string Fragment, string Cause)[] ObtainedCauses =
    [
        ("aucune consigne d'enfournement", "AC-FR20-4"),
        ("est introuvable dans L_D_COULEE", "AC-FR20-5"),
        ("l'OF existe déjà et ne peut pas être remplacé", "AC-FR20-6"),
        ("la répartition des lingots aux fours", "AC-FR20-2"),
    ];

    private static readonly string ProductionConnectionString =
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables("KAPE22_TEST_")
            .Build()
            .GetConnectionString("AscoLSI_Production") ?? string.Empty;

    private static readonly string[] ReferenceTables =
    [
        "L_P_CONSIGNES_CHUTAGE",
        "L_P_CONSIGNES_CODEOUTIL_COUPE",
        "L_P_CONSIGNES_DECOUPE",
        "L_P_CONSIGNES_DEGAZAGE_DETAIL",
        "L_P_CONSIGNES_DEGAZAGE_GLOBAL",
        "L_P_CONSIGNES_LINGOT",
        "L_P_CONSIGNES_MARQUAGE",
        "L_P_CONSIGNES_PITS",
        "L_P_CONSIGNES_POIDSMETRIQUE",
        "L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER",
        "L_P_CONSIGNES_REFROIDISSEMENT",
        "L_P_CONSIGNES_REFROIDISSOIRS",
        "L_P_CONSIGNES_SMQ",
    ];

    private static string ErrorDirectory => RepoLayout.ProjectFile(Path.Combine("P60", "error"));

    public static TheoryData<string> ErrorFichiers()
    {
        TheoryData<string> data = new();
        if (!Directory.Exists(ErrorDirectory))
        {
            return data;
        }

        foreach (string path in Directory.EnumerateFiles(ErrorDirectory)
                     .Where(p => FichierName.IsMatch(Path.GetFileName(p)))
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [SkippableTheory]
    [MemberData(nameof(ErrorFichiers))]
    [Trait("AC", "FR20-6")]
    public void RejectedFichier_HasTheLegacyRejectionCause_AcFr20_6(string fichierName)
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        Skip.If(
            string.IsNullOrWhiteSpace(ProductionConnectionString),
            "No production database configured. Set ConnectionStrings:AscoLSI_Production in " +
            "tests/Kape22Importer.Tests/appsettings.Test.json or KAPE22_TEST_ConnectionStrings__AscoLSI_Production.");

        LegacyTrace? trace = ReadLegacyTrace(fichierName);
        Skip.If(trace is null, $"{fichierName}: aucune trace legacy terminée en erreur dans L_D_LOG_COMMANDE.");
        string legacyReasons = string.Join(" | ", trace!.Reasons);
        string? expected = LegacyReasons
            .FirstOrDefault(reason => legacyReasons.Contains(reason.Fragment, StringComparison.OrdinalIgnoreCase)).Cause;
        Assert.True(expected is not null, $"{fichierName}: raison legacy inconnue : {legacyReasons}");

        byte[] content = File.ReadAllBytes(Path.Combine(ErrorDirectory, fichierName));
        RebuildState(trace, TestSupport.MapFichier(content, fichierName));
        ImportResult result = Processor(trace.Instant).Import(fichierName, content);

        string errors = string.Join(" ; ", result.Errors.Select(error => error.Message));
        string obtained = result.Success
            ? Accepted
            : ObtainedCauses.FirstOrDefault(cause => errors.Contains(cause.Fragment, StringComparison.Ordinal)).Cause ?? "inconnue";
        Assert.True(
            expected == obtained,
            $"{fichierName}: cause attendue {expected} (legacy : {legacyReasons}), cause obtenue {obtained} ({errors}).");

        if (expected == Accepted)
        {
            output.WriteLine($"{fichierName} : écart connu - refusé par le legacy (AC-FR20-3 retiré), accepté ici.");
        }
    }

    private static SqlConnection OpenProduction()
    {
        SqlConnection connection = new(new SqlConnectionStringBuilder(ProductionConnectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    // SELECT on production: the latest legacy run of this Fichier, or null when there is none or it did
    // not end in error.
    private static LegacyTrace? ReadLegacyTrace(string fichierName)
    {
        string name = fichierName.Replace("_", "[_]", StringComparison.Ordinal);
        using SqlConnection production = OpenProduction();

        using SqlCommand startCommand = new(
            "SELECT TOP 1 Id, [Date] FROM dbo.L_D_LOG_COMMANDE WITH (NOLOCK) " +
            "WHERE Commande = 'GPAO' AND Message LIKE @pattern ORDER BY Id DESC;",
            production);
        startCommand.Parameters.AddWithValue("@pattern", $"Traitement du fichier GPAO '%\\{name}'.");
        int start;
        DateTime instant;
        using (SqlDataReader reader = startCommand.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            start = reader.GetInt32(0);
            instant = reader.GetDateTime(1);
        }

        using SqlCommand endCommand = new(
            "SELECT TOP 1 Id, Message FROM dbo.L_D_LOG_COMMANDE WITH (NOLOCK) " +
            "WHERE Commande = 'GPAO' AND Id > @start AND Message LIKE @pattern ORDER BY Id;",
            production);
        endCommand.Parameters.AddWithValue("@start", start);
        endCommand.Parameters.AddWithValue("@pattern", $"%\\{name} s'est%");
        int end;
        using (SqlDataReader reader = endCommand.ExecuteReader())
        {
            if (!reader.Read() || !reader.GetString(1).Contains("terminé avec une erreur", StringComparison.Ordinal))
            {
                return null;
            }

            end = reader.GetInt32(0);
        }

        using SqlCommand reasonsCommand = new(
            "SELECT Message FROM dbo.L_D_LOG_COMMANDE WITH (NOLOCK) " +
            "WHERE Commande = 'KAP22' AND Id > @start AND Id < @end ORDER BY Id;",
            production);
        reasonsCommand.Parameters.AddWithValue("@start", start);
        reasonsCommand.Parameters.AddWithValue("@end", end);
        List<string> reasons = [];
        using (SqlDataReader reader = reasonsCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                reasons.Add(reader.GetString(0));
            }
        }

        return new LegacyTrace(instant, reasons, start);
    }

    // Resets AscoLSI_Test to the state the Fichier met in production (class header).
    private void RebuildState(LegacyTrace trace, Kape22ImportBundle bundle)
    {
        fixture.ResetData();
        foreach (string table in ReferenceTables)
        {
            CopyFromProduction(table, "1 = 1");
        }

        string? coulee = bundle.Coulee?.IdCoulee;
        if (coulee is not null)
        {
            CopyFromProduction(
                "L_D_COULEE",
                "IdCoulee = @coulee AND DateReception < @instant",
                new SqlParameter("@coulee", coulee),
                new SqlParameter("@instant", trace.Instant));
        }

        L_D_ORDRE_FABRICATION? ordreFabrication = bundle.OrdreFabrication;
        if (ordreFabrication is null)
        {
            return;
        }

        using SqlConnection production = OpenProduction();
        using SqlCommand command = new(
            "SELECT " +
            "CASE WHEN EXISTS (SELECT 1 FROM dbo.L_D_LOG_COMMANDE WITH (NOLOCK) WHERE Commande = 'GPAO' " +
            "AND [OF] IN (@of, @padded) AND Message = N'Création d''un OF' AND Id < @start) THEN 1 ELSE 0 END, " +
            "CASE WHEN EXISTS (SELECT 1 FROM dbo.L_D_LOG_COMMANDE WITH (NOLOCK) WHERE Commande = 'ENC' " +
            "AND [OF] IN (@of, @padded) AND Id < @start) THEN 1 ELSE 0 END;",
            production);
        command.Parameters.AddWithValue("@of", bundle.OF!.Trim());
        command.Parameters.AddWithValue("@padded", ordreFabrication.OF);
        command.Parameters.AddWithValue("@start", trace.Start);
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) == 0)
        {
            return;
        }

        ordreFabrication.Etat = reader.GetInt32(1);
        using AscoLsiDbContext context = fixture.NewAscoLsiContext();
        context.OrdreFabricationRows.Add(ordreFabrication);
        context.SaveChanges();
    }

    // SELECT on production, then one parameterized INSERT per row of the test table's own columns. Plain
    // INSERTs rather than SqlBulkCopy: the copied tables hold a few hundred rows at most, and a bulk insert
    // can wait indefinitely for a memory grant on a memory-starved local instance.
    private void CopyFromProduction(string table, string where, params SqlParameter[] parameters)
    {
        using SqlConnection test = new(fixture.AscoLsiConnectionString);
        test.Open();
        List<string> columns = [];
        bool hasIdentity = false;
        using (SqlCommand command = new(
            "SELECT name, is_identity FROM sys.columns WHERE object_id = OBJECT_ID(@table) ORDER BY column_id;", test))
        {
            command.Parameters.AddWithValue("@table", $"dbo.{table}");
            using SqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
                hasIdentity |= reader.GetBoolean(1);
            }
        }

        string columnList = string.Join(", ", columns.Select(column => $"[{column}]"));
        DataTable rows = new();
        using (SqlConnection production = OpenProduction())
        using (SqlCommand select = new($"SELECT {columnList} FROM dbo.{table} WITH (NOLOCK) WHERE {where};", production))
        {
            select.Parameters.AddRange(parameters);
            using SqlDataReader reader = select.ExecuteReader();
            rows.Load(reader);
        }

        string identity = hasIdentity ? $"SET IDENTITY_INSERT dbo.{table} ON; " : string.Empty;
        string values = string.Join(", ", columns.Select((_, index) => $"@p{index}"));
        foreach (DataRow row in rows.Rows)
        {
            using SqlCommand insert = new($"{identity}INSERT INTO dbo.{table} ({columnList}) VALUES ({values});", test);
            for (int index = 0; index < columns.Count; index++)
            {
                insert.Parameters.AddWithValue($"@p{index}", row[index]);
            }

            insert.ExecuteNonQuery();
        }
    }

    // The real processor on the test instance, its clock at the legacy run's instant.
    private Kape22FichierProcessor Processor(DateTime instant) =>
        new(
            fixture.NewAscoLsiContext,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:Commande"] = "P60",
                ["Import:InitiatingServer"] = SqlServerIntegrationFixture.JournalInitiatingServer,
            }).Build(),
            fixture.NewJournal(),
            new ImportOptions
            {
                ArchiveFolder = "archive",
                ErrorFolder = "error",
                InboxPath = "inbox",
                InitiatingServer = SqlServerIntegrationFixture.JournalInitiatingServer,
                PollingInterval = TimeSpan.FromSeconds(30),
                ProcessingFolder = "processing",
                RetentionDays = 30,
            },
            new FixedClock(new DateTimeOffset(instant, TimeZoneInfo.Local.GetUtcOffset(instant))),
            NullLogger<Kape22FichierProcessor>.Instance);

    // One legacy run of a Fichier: the instant and Id of its start line and its KAP22 reasons.
    private sealed record LegacyTrace(DateTime Instant, IReadOnlyList<string> Reasons, int Start);
}
