namespace Kape22Importer.Persistence;

// Database-first entity for AscoLSI.dbo.L_D_SECTIONCHARGE_LINGOT (Story 4.1): 19 columns, Lingot
// charge section per (OF, CodeOperation). Shapes are frozen here, there is no migration (AR-8, AR-12);
// sourced from AFV004-LSI sys.columns/sys.indexes on 2026-09-14, never written from memory (risk R-3).
// The key is the natural composite business key (OF, CodeOperation), not a surrogate identity. Only OF,
// CodeOperation and RangOperation are NOT NULL; every other column is nullable. Properties are declared
// in alphabetical order (CC-4).
public class L_D_SECTIONCHARGE_LINGOT
{
    public string CodeOperation { get; set; } = null!;

    public decimal? EpaisseurEnLaminage { get; set; }

    public string OF { get; set; } = null!;

    public int? PriseDeFer { get; set; }

    public decimal? PriseDeFerEpaisseur { get; set; }

    public decimal? PriseDeFerEpaisseurGPAO { get; set; }

    public decimal? PriseDeFerHauteur { get; set; }

    public decimal? PriseDeFerHauteurGPAO { get; set; }

    public int? PriseDeFerSection { get; set; }

    public int? PriseDeFerSectionGPAO { get; set; }

    public string? ProfileLamine { get; set; }

    public int? Programme { get; set; }

    public int? ProgrammeGPAO { get; set; }

    public string RangOperation { get; set; } = null!;

    public decimal? SectionLaminage { get; set; }

    public decimal? ToleranceMaxEpaisseur { get; set; }

    public decimal? ToleranceMaxSection { get; set; }

    public decimal? ToleranceMinEpaisseur { get; set; }

    public decimal? ToleranceMinSection { get; set; }
}
