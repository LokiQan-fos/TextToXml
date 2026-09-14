using System;

namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_SECTIONCHARGE_PITS (Story 4.1): 10 columns, Pits charge
// section per (OF, CodeOperation). Shapes are frozen here, there is no migration (AR-8, AR-12); sourced
// from AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3). The key
// is the natural composite business key (OF, CodeOperation), not a surrogate identity. Only OF,
// CodeOperation and RangOperation are NOT NULL; every other column is nullable. Properties are declared
// in alphabetical order (CC-4).
public class L_D_SECTIONCHARGE_PITS
{
    public string CodeOperation { get; set; } = null!;

    public DateTime? DateDefournementFour1 { get; set; }

    public DateTime? DateDefournementFour2 { get; set; }

    public DateTime? DateEnfournementFour1 { get; set; }

    public DateTime? DateEnfournementFour2 { get; set; }

    public decimal? H2Coulee { get; set; }

    public int? NumeroFour1 { get; set; }

    public int? NumeroFour2 { get; set; }

    public string OF { get; set; } = null!;

    public string RangOperation { get; set; } = null!;
}
