using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.13 (AC-FR19-6): pure port of the legacy OrdreFabrication.BuildLibelleConsigne
// (Desktop/kape22/OrdreFabrication.cs:700-1222, read-only reference, AD-3, never called at runtime). One
// method per legacy case: it concatenates the labels of the section's own rows, in the legacy __tabCodes*
// order, each followed by the legacy separator, the last one included. A type the section did not produce
// contributes nothing, not even its separator: the legacy Find returns null and the caught
// NullReferenceException appends string.Empty. The rows passed in carry the GetLibelle labels resolved
// before any composite is written, the same values the legacy Find reads.
public static class LibelleConsigneComposer
{
    private const string Separator = "\t\t";

    // ConsignesChutage (OrdreFabrication.cs:868-951).
    public static string Chutage(IReadOnlyList<L_D_CONSIGNES> rows) => Join(rows, 0, 1, 2, 3, 4);

    // ConsignesDecoupeLingot (OrdreFabrication.cs:957-1079): 16 and 17 build the size-12 label (type 13),
    // 24 to 28 the size-18 label (type 24). Type 29 is not in the legacy list.
    public static (string SizeTwelve, string SizeEighteen) Decoupe(IReadOnlyList<L_D_CONSIGNES> rows) =>
        (Join(rows, 16, 17), Join(rows, 24, 25, 26, 27, 28));

    // ConsignesLingot (OrdreFabrication.cs:788-862).
    public static string Lingot(IReadOnlyList<L_D_CONSIGNES> rows) => Join(rows, 15, 7, 8, 9);

    // ConsignesEnfournementPits (OrdreFabrication.cs:709-782): 12, then 10 and 11 with "°C", 14 is a no-op.
    // The type 5 label follows with no separator, then "\n" and the type 6 label when it is non-empty.
    // Without a type 5 row the legacy dereferences null inside the try that also assigns the type 13
    // label, so that label is never assigned: null tells the caller to leave the row as it is.
    public static string? Pits(IReadOnlyList<L_D_CONSIGNES> rows)
    {
        L_D_CONSIGNES? hold = Find(rows, 5);
        if (hold is null)
        {
            return null;
        }

        string libelle = Join(rows, 12) + Join(rows, "°C" + Separator, 10, 11) + hold.LibelleConsigne;
        string? preheat = Find(rows, 6)?.LibelleConsigne;
        if (!string.IsNullOrEmpty(preheat))
        {
            libelle += "\n" + preheat;
        }

        return libelle;
    }

    // ConsignesPoidsMetrique (OrdreFabrication.cs:1085-1146).
    public static string PoidsMetrique(IReadOnlyList<L_D_CONSIGNES> rows) => Join(rows, 18, 19, 20);

    // ConsignesRefroidissoir (OrdreFabrication.cs:1152-1213).
    public static string Refroidissoirs(IReadOnlyList<L_D_CONSIGNES> rows) => Join(rows, 21, 22, 23);

    // The legacy List.Find: the first row of that type.
    private static L_D_CONSIGNES? Find(IReadOnlyList<L_D_CONSIGNES> rows, int typeConsigne) =>
        rows.FirstOrDefault(row => row.TypeConsigne == typeConsigne);

    private static string Join(IReadOnlyList<L_D_CONSIGNES> rows, params int[] typeConsignes) =>
        Join(rows, Separator, typeConsignes);

    private static string Join(IReadOnlyList<L_D_CONSIGNES> rows, string suffix, params int[] typeConsignes)
    {
        string libelle = string.Empty;
        foreach (int typeConsigne in typeConsignes)
        {
            L_D_CONSIGNES? row = Find(rows, typeConsigne);
            if (row is not null)
            {
                libelle += row.LibelleConsigne + suffix;
            }
        }

        return libelle;
    }
}
