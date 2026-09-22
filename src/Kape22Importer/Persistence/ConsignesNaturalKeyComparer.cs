using System;
using System.Collections.Generic;

namespace Kape22Importer.Persistence;

// C-2 (Épic 4 retro #3): the A-5 pre-check's natural-key equality - CodeOperation collides
// case-insensitively at the real SQL Server (case-insensitive collation), so the in-memory GroupBy must
// match that instead of plain Ordinal tuple equality, which would let "XC1" and "xc1" sail through as two
// distinct groups. OF, TypeConsigne and ConsigneGPAO stay Ordinal/exact - only CodeOperation is
// case-insensitive.
internal sealed class ConsignesNaturalKeyComparer : IEqualityComparer<(string OF, string CodeOperation, int TypeConsigne, bool ConsigneGPAO)>
{
    public static readonly ConsignesNaturalKeyComparer Instance = new();

    public bool Equals(
        (string OF, string CodeOperation, int TypeConsigne, bool ConsigneGPAO) x,
        (string OF, string CodeOperation, int TypeConsigne, bool ConsigneGPAO) y) =>
        x.OF == y.OF
        && string.Equals(x.CodeOperation, y.CodeOperation, StringComparison.OrdinalIgnoreCase)
        && x.TypeConsigne == y.TypeConsigne
        && x.ConsigneGPAO == y.ConsigneGPAO;

    public int GetHashCode((string OF, string CodeOperation, int TypeConsigne, bool ConsigneGPAO) key) =>
        HashCode.Combine(
            key.OF,
            StringComparer.OrdinalIgnoreCase.GetHashCode(key.CodeOperation),
            key.TypeConsigne,
            key.ConsigneGPAO);
}
