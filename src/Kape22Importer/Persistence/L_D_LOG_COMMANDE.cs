using System;

namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_LOG_COMMANDE (Annexe C.2): the business log, one row per
// processed file. Every column is NOT NULL except Trace. No migration (AR-8). Properties are declared
// in alphabetical order (CC-4).
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
