using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kape22Importer;

// Story 4.12 (AC-FR19-5): a pure, line-for-line port of the legacy LibelleConsigneController.GetLibelle
// (Lsi.Net/Ascometal.LSI.DAL/LibelleConsigneController.cs:14-285, read-only reference, AD-3, never
// referenced or called at runtime) plus its call-site rule (Desktop/kape22/OrdreDeFabricationManager.cs:
// 1405-1416): type 22 also takes ProfilProduit, DiametreProduit and H2Coulee, and a blank result becomes
// "?". Every legacy database query becomes a lookup in a ConsigneReferenceData snapshot, so this class
// reads no database (AD-2). The legacy quirks are kept on purpose: any exception gives "?", a PC1 lookup
// appends " " plus the particular preheating label (so a trailing space survives), numbers are formatted
// with the fr-FR culture the legacy server ran under, and an unmatched section, type or code gives "?".
// A code is matched the way the legacy SQL WHERE clause matched it on a French_CI_AS column: trailing
// spaces ignored (the nchar columns are padded) and case ignored; among several matches the first one in
// snapshot order wins, which ConsigneReferenceData.Load reads in (Section, Consignes, CodeConsigne) order.
public static class LibelleConsigneResolver
{
    private const string Unknown = "?";

    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // The legacy regular expressions, verbatim, including the literal comma inside the XC1 classes.
    private static readonly Regex ChutageDecimal = new("[0,A-Z][0-9]", RegexOptions.CultureInvariant);

    private static readonly Regex ChutageInteger = new("[0,A-Z]", RegexOptions.CultureInvariant);

    private static readonly Regex Digits = new("[0-9]+", RegexOptions.CultureInvariant);

    private static readonly LibelleConsigneResolution Unresolved = new() { Libelle = Unknown };

    // Resolves the label of one L_D_CONSIGNES row. The section argument is the row's CodeOperation. The
    // profilProduit, diametreProduit and h2Coulee arguments are only read for type 22, where a missing
    // diametreProduit or h2Coulee gives "?" exactly like the legacy decimal.Parse of an empty string threw
    // into its catch.
    public static LibelleConsigneResolution Resolve(
        string section,
        int typeConsigne,
        string codeConsigne,
        ConsigneReferenceData referenceData,
        string? profilProduit = null,
        decimal? diametreProduit = null,
        decimal? h2Coulee = null)
    {
        ArgumentNullException.ThrowIfNull(referenceData);

        LibelleConsigneResolution resolved;
        try
        {
            resolved = GetLibelle(section, typeConsigne, codeConsigne, referenceData, profilProduit, diametreProduit, h2Coulee);
        }
        catch (Exception)
        {
            // Legacy GetLibelle catches every exception (logging it) and returns "?".
            resolved = Unresolved;
        }

        // The call-site rule (OrdreDeFabricationManager.cs:1405-1416): a blank label becomes "?".
        return string.IsNullOrEmpty(resolved.Libelle.Trim()) ? resolved with { Libelle = Unknown } : resolved;
    }

    private static LibelleConsigneResolution GetLibelle(
        string section,
        int type,
        string code,
        ConsigneReferenceData data,
        string? profilProduit,
        decimal? diametreProduit,
        decimal? h2Coulee)
    {
        switch (section)
        {
            case "PC1":
                return Pits(section, type, code, data);
            case "LA9":
            case "LA1":
                return type == 15
                    ? Computed(string.Concat(code.Substring(0, 3).Trim(), "0"))
                    : Lookup(data.Lingot, section, type, code);
            case "XC1":
                return Chutage(section, type, code, data);
            case "XP1":
                if (type == 16 || type == 17 || type == 26)
                {
                    return Thousandths(code);
                }

                if (type == 25 || type == 27 || type == 28 || type == 29)
                {
                    return Computed(code);
                }

                return Lookup(data.Decoupe, section, type, code);
            case "XP9":
                if (type == 18)
                {
                    return Thousandths(code);
                }

                if (type == 19 || type == 20)
                {
                    return Computed(code);
                }

                return Lookup(data.PoidsMetrique, section, type, code);
            case "FD1":
            case "FD2":
            case "FD3":
            case "FC1":
            case "XA2":
            case "XA1":
                return Refroidissoirs(section, type, code, data, profilProduit, diametreProduit, h2Coulee);
            default:
                return Unresolved;
        }
    }

    // PC1 (legacy line 25): types 10 and 11 are computed temperatures, every other type is a
    // L_P_CONSIGNES_PITS lookup; type 6 first swaps a leading digit of 2 or more for "0" and appends that
    // digit's L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER label.
    private static LibelleConsigneResolution Pits(string section, int type, string code, ConsigneReferenceData data)
    {
        if (type == 10)
        {
            int value = 1000 + (int.Parse(code.Substring(0, 2), French) * 10) + (int.Parse(code.Substring(2, 1), French) * 10);
            return Computed(value.ToString(French));
        }

        if (type == 11)
        {
            return Computed((1000 + (int.Parse(code.Substring(0, 2), French) * 10)).ToString(French));
        }

        string particularSuffix = string.Empty;
        DateTime? particularDateMaj = null;
        string codeConsigne = code;
        if (type == 6)
        {
            if (!string.IsNullOrEmpty(code) && !code[0].Equals('0') && int.Parse(code[0].ToString(), French) >= 2)
            {
                int particularCode = int.Parse(code.Substring(0, 1), French);
                PrechauffageParticulierReference? particular =
                    data.PrechauffageParticulier.FirstOrDefault(row => row.Code == particularCode);
                if (particular is not null)
                {
                    particularSuffix = particular.Libelle;
                    particularDateMaj = particular.DateMaj;
                }
            }

            codeConsigne = "0" + codeConsigne.Substring(1);
        }

        SectionConsigneReference? match = FirstMatch(data.Pits, section, type, codeConsigne);
        if (match is null)
        {
            return Unresolved;
        }

        return new LibelleConsigneResolution
        {
            LatestDateMaj = Latest(match.DateMaj, particularDateMaj),
            Libelle = match.Libelle + " " + particularSuffix,
        };
    }

    // XC1 (legacy line 80): types 0 and 1 decode a letter-coded number, 2 is a
    // L_P_CONSIGNES_CODEOUTIL_COUPE lookup, 4 a L_P_CONSIGNES_MARQUAGE lookup, any other type a
    // L_P_CONSIGNES_CHUTAGE lookup. A type 0 or 1 code the expression does not match gives "?".
    private static LibelleConsigneResolution Chutage(string section, int type, string code, ConsigneReferenceData data)
    {
        if (type == 0)
        {
            if (!string.IsNullOrEmpty(code) && ChutageDecimal.IsMatch(code))
            {
                int integerPart = code[0] == '0' ? 0 : (code[0] - 'A') + 1;
                int decimalPart = int.Parse(code[1].ToString(), French);
                return Computed(string.Format(French, "{0},{1}", integerPart, decimalPart));
            }

            return Unresolved;
        }

        if (type == 1)
        {
            if (!string.IsNullOrEmpty(code) && ChutageInteger.IsMatch(code))
            {
                int integerPart = code[0] == '0' ? 0 : (code[0] - 'A') + 1;
                return Computed(integerPart.ToString(French));
            }

            return Unresolved;
        }

        if (type == 2)
        {
            return Lookup(data.CodeOutilCoupe, section, type, code);
        }

        if (type == 4)
        {
            MarquageReference? marquage = data.Marquage.FirstOrDefault(
                row => SqlEquals(row.Section, section) && row.Consignes == type && SqlEquals(row.CodeConsigne, code));
            if (marquage is null)
            {
                return Unresolved;
            }

            return new LibelleConsigneResolution
            {
                LatestDateMaj = marquage.DateMaj,
                Libelle = "section: " + marquage.LibelleSection + " - Pied: " + marquage.LibellePied + " - Tête: " + marquage.LibelleTete,
            };
        }

        return Lookup(data.Chutage, section, type, code);
    }

    // FD1/FD2/FD3/FC1/XA2/XA1 (legacy line 190): 21 is a L_P_CONSIGNES_REFROIDISSEMENT lookup, 23 a
    // L_P_CONSIGNES_SMQ lookup, 22 the degassing duration, any other type a L_P_CONSIGNES_REFROIDISSOIRS
    // lookup.
    private static LibelleConsigneResolution Refroidissoirs(
        string section,
        int type,
        string code,
        ConsigneReferenceData data,
        string? profilProduit,
        decimal? diametreProduit,
        decimal? h2Coulee)
    {
        if (type == 21)
        {
            return CodeLookup(data.Refroidissement, code);
        }

        if (type == 23)
        {
            return CodeLookup(data.Smq, code);
        }

        if (type == 22)
        {
            return Degazage(code, data, profilProduit, diametreProduit, h2Coulee);
        }

        return Lookup(data.Refroidissoirs, section, type, code);
    }

    // Type 22 (legacy line 217): with a known degassing Code, the first L_P_CONSIGNES_DEGAZAGE_DETAIL
    // row of that Code and ProfilProduit whose (SectionMin, SectionMax] range holds DiametreProduit gives
    // H21..H24 by H2Coulee bucket, suffixed "h"; every other case gives "0". Neither degassing table has a
    // DateMaj, so no date is reported.
    private static LibelleConsigneResolution Degazage(
        string code,
        ConsigneReferenceData data,
        string? profilProduit,
        decimal? diametreProduit,
        decimal? h2Coulee)
    {
        int degassingCode = int.Parse(code, French);
        if (diametreProduit is not decimal diameter || h2Coulee is not decimal h2)
        {
            return Unresolved;
        }

        if (data.DegazageGlobal.Contains(degassingCode))
        {
            DegazageDetailReference? detail = data.DegazageDetail.FirstOrDefault(row =>
                row.Code == degassingCode
                && profilProduit is not null
                && SqlEquals(row.ProfilProduit, profilProduit)
                && diameter > row.SectionMin
                && diameter <= row.SectionMax);
            if (detail is not null)
            {
                if (h2 > 2.5m && h2 <= 3m)
                {
                    return Computed(detail.H21.ToString(French) + "h");
                }

                if (h2 > 3m && h2 <= 4m)
                {
                    return Computed(detail.H22.ToString(French) + "h");
                }

                if (h2 > 4m && h2 <= 5m)
                {
                    return Computed(detail.H23.ToString(French) + "h");
                }

                if (h2 > 5m)
                {
                    return Computed(detail.H24.ToString(French) + "h");
                }
            }
        }

        return Computed("0");
    }

    // XP1 16/17/26 and XP9 18 (legacy lines 140 and 166): a digit-bearing code read as thousandths,
    // through the same float conversion, formatted "0.000" in fr-FR ("11400" gives "11,400").
    private static LibelleConsigneResolution Thousandths(string code)
    {
        if (!string.IsNullOrEmpty(code) && Digits.IsMatch(code))
        {
            int integerPart = int.Parse(code, French);
            float value = (float)(integerPart / 1000.000);
            return Computed(value.ToString("0.000", French));
        }

        return Unresolved;
    }

    private static LibelleConsigneResolution Lookup(
        IReadOnlyList<SectionConsigneReference> rows, string section, int type, string code)
    {
        SectionConsigneReference? match = FirstMatch(rows, section, type, code);
        return match is null ? Unresolved : new LibelleConsigneResolution { LatestDateMaj = match.DateMaj, Libelle = match.Libelle };
    }

    private static LibelleConsigneResolution CodeLookup(IReadOnlyList<CodeConsigneReference> rows, string code)
    {
        CodeConsigneReference? match = rows.FirstOrDefault(row => SqlEquals(row.Code, code));
        return match is null ? Unresolved : new LibelleConsigneResolution { LatestDateMaj = match.DateMaj, Libelle = match.Libelle };
    }

    private static SectionConsigneReference? FirstMatch(
        IReadOnlyList<SectionConsigneReference> rows, string section, int type, string code) =>
        rows.FirstOrDefault(row => SqlEquals(row.Section, section) && row.Consignes == type && SqlEquals(row.CodeConsigne, code));

    // SQL Server '=' on a French_CI_AS column: trailing spaces and case are ignored.
    private static bool SqlEquals(string left, string right) =>
        string.Equals(left.TrimEnd(' '), right.TrimEnd(' '), StringComparison.OrdinalIgnoreCase);

    private static LibelleConsigneResolution Computed(string libelle) => new() { Libelle = libelle };

    private static DateTime? Latest(DateTime? first, DateTime? second) =>
        first is null ? second : second is null ? first : (first > second ? first : second);
}

// The label LibelleConsigneResolver computed for one L_D_CONSIGNES row, with the latest DateMaj among
// the reference rows it consulted (null for a computed label or a miss). Properties are declared in
// alphabetical order (CC-4).
public sealed record LibelleConsigneResolution
{
    public DateTime? LatestDateMaj { get; init; }

    public required string Libelle { get; init; }
}
