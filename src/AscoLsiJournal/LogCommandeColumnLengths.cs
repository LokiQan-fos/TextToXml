namespace AscoLsiJournal;

// The character max_length of every bounded string column of L_D_LOG_COMMANDE, taken from Annexe C.2 /
// scripts/schema/01-ascolsi-tables.sql. Message is NVARCHAR(MAX) and carries no entry.
public static class LogCommandeColumnLengths
{
    public const int Commande = 50;

    public const int OF = 12;

    public const int User = 50;
}
