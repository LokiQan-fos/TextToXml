using System;
using System.Linq;
using System.Transactions;
using FichierJournal;

namespace AscoLsiJournal;

// The LSI implementation of IFichierJournal (D31, FR-23): one L_D_LOG_COMMANDE row per entry, written in
// a fresh context with its own SaveChanges, outside any business transaction. The row follows D8:
// "<NumeroFichier> — OK" or "<NumeroFichier> — REJETÉ : <reasons>", the entry's Commande and raw OF, the
// instant in Paris time, NumLingot 0 and Trace 1 (keeps the row in the AscoLSI business-log views,
// Annexe C.2). Without a readable OF no row can be written (D15). A failed write propagates. HasSuccess
// looks for that same "— OK" row (Story 6.1, D22 revised).
public sealed class AscoLsiFichierJournal : IFichierJournal
{
    private const string InitiatingServerSetting = "InitiatingServer";

    private readonly Func<AscoLsiJournalDbContext> newContext;
    private readonly string user;

    public AscoLsiFichierJournal(Func<AscoLsiJournalDbContext> newContext, string? initiatingServer)
    {
        this.newContext = newContext;
        this.user = ResolveUser(initiatingServer);
    }

    // The success row is the "— OK" message for the entry's Commande and OF; without a readable OF no row
    // can exist (D15).
    public bool HasSuccess(FichierJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.OF))
        {
            return false;
        }

        string okMessage = OkMessage(entry);
        using TransactionScope independent = new(TransactionScopeOption.Suppress);
        using AscoLsiJournalDbContext context = this.newContext();
        bool found = context.LogCommandeRows.Any(
            row => row.Commande == entry.Commande && row.OF == entry.OF && row.Message == okMessage);
        independent.Complete();
        return found;
    }

    public void Record(FichierJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.OF))
        {
            return;
        }

        string message = entry.Reasons.Count == 0
            ? OkMessage(entry)
            : $"{MessageHead(entry)} — REJETÉ : {string.Join(" ; ", entry.Reasons)}";

        // Suppress any ambient TransactionScope: the journal records the result of the import, so a caller
        // rolling back its business transaction must not roll the journal row back with it (D31).
        using TransactionScope independent = new(TransactionScopeOption.Suppress);
        using AscoLsiJournalDbContext context = this.newContext();
        context.LogCommandeRows.Add(new L_D_LOG_COMMANDE
        {
            Commande = entry.Commande,
            Date = TimeZoneInfo.ConvertTimeFromUtc(entry.Instant.UtcDateTime, ParisTime.Zone),
            Message = message,
            NumLingot = 0,
            OF = entry.OF,
            Trace = true,
            User = this.user,
        });
        context.SaveChanges();
        independent.Complete();
    }

    // A readable OF with an unreadable NumeroFichier (P89.xsd allows a blank one) still names the
    // Fichier: the message starts with its FichierName instead (F-1 of the Story 5.0 review).
    private static string MessageHead(FichierJournalEntry entry) =>
        string.IsNullOrWhiteSpace(entry.NumeroFichier) ? entry.FichierName : entry.NumeroFichier;

    private static string OkMessage(FichierJournalEntry entry) => $"{MessageHead(entry)} — OK";

    // L_D_LOG_COMMANDE.User is NOT NULL: a blank setting falls back to the machine name, and an over-long
    // one fails here, at construction, rather than on every write.
    private static string ResolveUser(string? configured)
    {
        string user = string.IsNullOrWhiteSpace(configured) ? Environment.MachineName : configured;
        if (user.Length > LogCommandeColumnLengths.User)
        {
            throw new ArgumentException(
                $"'{InitiatingServerSetting}' resolves to {user.Length} characters; L_D_LOG_COMMANDE.User holds {LogCommandeColumnLengths.User}.",
                "initiatingServer");
        }

        return user;
    }
}
