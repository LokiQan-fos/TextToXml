namespace Kape22Importer.Persistence;

// The character max_length of every bounded string column of L_D_LOG_COMMANDE, taken once from Annexe
// C.2 / scripts/schema/01-ascolsi-tables.sql. AscoLsiDbContext applies them with HasMaxLength and
// Kape22Persister validates the configured User against them, so an over-long value fails with a clear
// message instead of only at the database. Message is NVARCHAR(MAX) and carries no entry.
public static class LogCommandeColumnLengths
{
    public const int Commande = 50;

    public const int OF = 12;

    public const int User = 50;
}
