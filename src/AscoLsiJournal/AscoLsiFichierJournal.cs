using System;
using System.Transactions;
using FichierJournal;

namespace AscoLsiJournal;

// The LSI implementation of IFichierJournal (D31, FR-23): one L_D_LOG_COMMANDE row per entry, written in
// a fresh context with its own SaveChanges, outside any business transaction. The row follows D8:
// "<NumeroFichier> — OK" or "<NumeroFichier> — REJETÉ : <reasons>", the entry's Commande and raw OF, the
// instant in Paris time, NumLingot 0 and Trace 1 (keeps the row in the AscoLSI business-log views,
// Annexe C.2). Without a readable OF no row can be written (D15). A failed write propagates.
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

    public void Record(FichierJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.OF))
        {
            return;
        }

        string numeroFichier = entry.NumeroFichier ?? string.Empty;
        string message = entry.Reasons.Count == 0
            ? $"{numeroFichier} — OK"
            : $"{numeroFichier} — REJETÉ : {string.Join(" ; ", entry.Reasons)}";

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
