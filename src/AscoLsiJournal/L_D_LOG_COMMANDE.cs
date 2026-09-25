using System;

namespace AscoLsiJournal;

// Database-first entity for AscoLSI.dbo.L_D_LOG_COMMANDE (Annexe C.2): the LSI business log, one row per
// processed Fichier. Every column is NOT NULL except Trace. No migration (AD-5). Properties are declared
// in alphabetical order (CC-4). Kape22Importer keeps its own copy until the P60 journal migration.
public class L_D_LOG_COMMANDE
{
    public string Commande { get; set; } = null!;

    public DateTime Date { get; set; }

    public int Id { get; set; }

    public string Message { get; set; } = null!;

    public int NumLingot { get; set; }

    public string OF { get; set; } = null!;

    public bool? Trace { get; set; }

    public string User { get; set; } = null!;
}
