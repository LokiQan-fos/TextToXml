using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kape22Importer;

// Story 4.12 (AC-FR19-5): an immutable, in-memory snapshot of the 13 AscoLSI L_P_CONSIGNES_* reference
// tables that the legacy LibelleConsigneController.GetLibelle read one query at a time. Kape22FichierProcessor
// loads it once per Fichier from that Fichier's own AscoLsiDbContext, so LibelleConsigneResolver and
// ConsignesMapper stay pure (AD-2). The tables are only ever read (SELECT) and never written by the dispatch,
// so they are read through Database.SqlQuery into the keyless records below rather than mapped as DbSet
// entities. Column shapes come from AFV004-LSI sys.columns (2026-09-24, risk R-3). Every row keeps its
// DateMaj, which the production parity test compares against the Fichier's DateReception. Properties are
// declared in alphabetical order (CC-4).
public sealed record ConsigneReferenceData
{
    // The 2 tables keyed by a padded nchar Code alone (REFROIDISSEMENT, SMQ) share this column list.
    private const string CodeOrder = " ORDER BY Code";

    private const string CodeSelect = "SELECT Code, DateMaj, Libelle FROM dbo.";

    // The 7 section-keyed tables share this column list, read in the (Section, Consignes, CodeConsigne)
    // order the resolver's "first match" rule relies on.
    private const string SectionOrder = " ORDER BY Section, Consignes, CodeConsigne";

    private const string SectionSelect = "SELECT CodeConsigne, Consignes, DateMaj, Libelle, Section FROM dbo.";

    public IReadOnlyList<SectionConsigneReference> Chutage { get; init; } = [];

    public IReadOnlyList<SectionConsigneReference> CodeOutilCoupe { get; init; } = [];

    public IReadOnlyList<SectionConsigneReference> Decoupe { get; init; } = [];

    public IReadOnlyList<DegazageDetailReference> DegazageDetail { get; init; } = [];

    // L_P_CONSIGNES_DEGAZAGE_GLOBAL is only ever tested for the existence of a Code, so only its Codes
    // are kept.
    public IReadOnlyList<int> DegazageGlobal { get; init; } = [];

    // The snapshot of a context with no reference table at all: every lookup-based libellé resolves to
    // "?", every computed one is still produced.
    public static ConsigneReferenceData Empty { get; } = new();

    public IReadOnlyList<SectionConsigneReference> Lingot { get; init; } = [];

    public IReadOnlyList<MarquageReference> Marquage { get; init; } = [];

    public IReadOnlyList<SectionConsigneReference> Pits { get; init; } = [];

    public IReadOnlyList<SectionConsigneReference> PoidsMetrique { get; init; } = [];

    public IReadOnlyList<PrechauffageParticulierReference> PrechauffageParticulier { get; init; } = [];

    public IReadOnlyList<CodeConsigneReference> Refroidissement { get; init; } = [];

    public IReadOnlyList<SectionConsigneReference> Refroidissoirs { get; init; } = [];

    public IReadOnlyList<CodeConsigneReference> Smq { get; init; } = [];

    // Reads the 13 reference tables through the given context, SELECT only. A non-relational provider
    // (the EF in-memory context the Unit tests use) has no reference table to read, so it yields Empty.
    public static ConsigneReferenceData Load(AscoLsiDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Database.IsRelational())
        {
            return Empty;
        }

        return new ConsigneReferenceData
        {
            Chutage = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_CHUTAGE" + SectionOrder),
            CodeOutilCoupe = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_CODEOUTIL_COUPE" + SectionOrder),
            Decoupe = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_DECOUPE" + SectionOrder),
            DegazageDetail = Query<DegazageDetailReference>(
                context,
                "SELECT Code, H21, H22, H23, H24, Id, ProfilProduit, SectionMax, SectionMin "
                + "FROM dbo.L_P_CONSIGNES_DEGAZAGE_DETAIL ORDER BY Id"),
            DegazageGlobal = Query<int>(context, "SELECT Code AS Value FROM dbo.L_P_CONSIGNES_DEGAZAGE_GLOBAL ORDER BY Code"),
            Lingot = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_LINGOT" + SectionOrder),
            Marquage = Query<MarquageReference>(
                context,
                "SELECT CodeConsigne, Consignes, DateMaj, Libelle_Pied AS LibellePied, "
                + "Libelle_Section AS LibelleSection, Libelle_Tete AS LibelleTete, Section "
                + "FROM dbo.L_P_CONSIGNES_MARQUAGE" + SectionOrder),
            Pits = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_PITS" + SectionOrder),
            PoidsMetrique = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_POIDSMETRIQUE" + SectionOrder),
            PrechauffageParticulier = Query<PrechauffageParticulierReference>(
                context,
                "SELECT Code, DateMaj, Libelle FROM dbo.L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER ORDER BY Code"),
            Refroidissement = Query<CodeConsigneReference>(context, CodeSelect + "L_P_CONSIGNES_REFROIDISSEMENT" + CodeOrder),
            Refroidissoirs = Query<SectionConsigneReference>(context, SectionSelect + "L_P_CONSIGNES_REFROIDISSOIRS" + SectionOrder),
            Smq = Query<CodeConsigneReference>(context, CodeSelect + "L_P_CONSIGNES_SMQ" + CodeOrder),
        };
    }

    // Every query text is a compile-time constant of this file, never external input. sql is passed
    // through a parameter only so the 7 section-keyed tables and the 2 code-keyed tables share their
    // column list and ordering.
    private static List<T> Query<T>(AscoLsiDbContext context, string sql) => [.. context.Database.SqlQueryRaw<T>(sql)];
}

// A row of L_P_CONSIGNES_REFROIDISSEMENT or L_P_CONSIGNES_SMQ. Code is an nchar column, so it carries
// its trailing space padding.
public sealed record CodeConsigneReference
{
    public string Code { get; init; } = string.Empty;

    public DateTime? DateMaj { get; init; }

    public string Libelle { get; init; } = string.Empty;
}

// A row of L_P_CONSIGNES_DEGAZAGE_DETAIL; the table has no DateMaj column.
public sealed record DegazageDetailReference
{
    public int Code { get; init; }

    public decimal H21 { get; init; }

    public decimal H22 { get; init; }

    public decimal H23 { get; init; }

    public decimal H24 { get; init; }

    public int Id { get; init; }

    public string ProfilProduit { get; init; } = string.Empty;

    public decimal? SectionMax { get; init; }

    public decimal? SectionMin { get; init; }
}

// A row of L_P_CONSIGNES_MARQUAGE, whose libellé is spread over 3 columns.
public sealed record MarquageReference
{
    public string CodeConsigne { get; init; } = string.Empty;

    public int Consignes { get; init; }

    public DateTime DateMaj { get; init; }

    public string LibellePied { get; init; } = string.Empty;

    public string LibelleSection { get; init; } = string.Empty;

    public string LibelleTete { get; init; } = string.Empty;

    public string Section { get; init; } = string.Empty;
}

// A row of L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER, keyed by an int Code.
public sealed record PrechauffageParticulierReference
{
    public int Code { get; init; }

    public DateTime? DateMaj { get; init; }

    public string Libelle { get; init; } = string.Empty;
}

// A row of one of the 7 section-keyed tables. Consignes is the column behind legacy TypeConsigne.id.
public sealed record SectionConsigneReference
{
    public string CodeConsigne { get; init; } = string.Empty;

    public int Consignes { get; init; }

    public DateTime DateMaj { get; init; }

    public string Libelle { get; init; } = string.Empty;

    public string Section { get; init; } = string.Empty;
}
