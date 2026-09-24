using System;
using System.Collections.Generic;
using System.Linq;
using Kape22Importer.Persistence;

namespace Kape22Importer;

// Story 4.4-bis (correction to Story 4.4, FR-19/AC-FR19-3): a single "full code" row per applicable
// section is not what production carries - legacy (OrdreDeFabricationManager.CompleteConsignes2 /
// AddOrModifyConsigne, OrdreDeFabricationManager.cs:1362-1682, read-only reference, AD-3, never
// called/referenced at runtime) also decodes each section's raw consigne code into several positional
// sub-fields, each its own row, at the exact offsets sourced from that legacy code. Story 4.13
// (AC-FR19-6, sprint-change-proposal-2026-09-24, which corrects the 2026-09-23 meaning): each decoded row
// exists twice, as AddOrModifyConsigne writes it (OrdreDeFabricationManager.cs:1362-1435). ConsigneGPAO=true
// is the value received from the GPAO, never edited. ConsigneGPAO=false is the working copy the import
// itself creates with the same codes, which operators edit afterwards (MCC). A section produces
// rows iff its own mapper produced a row (the same per-OF applicability rule, AC-FR19-2) AND its own raw
// consigne code is non-blank - a blank code decodes to nothing in legacy either (CompleteConsignes2's own
// "if (!string.IsNullOrEmpty(_global))" guard around both the full code and every sub-field). That blank-code
// rule covers the 6 decoded sections only: SVT keeps its unchanged single row whatever its code holds.
public static class ConsignesMapper
{
    public static List<L_D_CONSIGNES> Map(
        L_D_KAPE22 source,
        L_D_SECTIONCHARGE_CHUTAGE? chutage,
        L_D_SECTIONCHARGE_DECOUPE? decoupe,
        L_D_SECTIONCHARGE_LINGOT? lingot,
        L_D_SECTIONCHARGE_PITS? pits,
        L_D_SECTIONCHARGE_POIDSMETRIQUE? poidsMetrique,
        L_D_SECTIONCHARGE_REFROIDISSOIRS? refroidissoirs,
        L_D_SECTIONCHARGE_SVT? svt,
        L_D_ORDRE_FABRICATION? ordreFabrication = null,
        ConsigneReferenceData? referenceData = null)
    {
        List<L_D_CONSIGNES> consignes = [];

        if (chutage is not null)
        {
            // XC1 (OrdreDeFabricationManager.cs:1520-1547): full code (13) plus 5 sub-fields.
            AddSection(
                consignes, source.OF, chutage.CodeOperation, source.CodeConsigneChutage,
                (0, raw => raw.Substring(0, 2)),
                (1, raw => raw.Substring(3, 1)),
                (2, raw => raw.Substring(5, 1).Trim()),
                (3, raw => raw.Substring(7, 2).Trim()),
                (4, raw => raw.Substring(10, 1).Trim()));
        }

        if (decoupe is not null)
        {
            AddDecoupe(consignes, source.OF, decoupe.CodeOperation, source.CodeConsigneDecoupe, source.LibelleConsigneDecoupe);
        }

        if (lingot is not null)
        {
            // LA1 (OrdreDeFabricationManager.cs:1488-1514): full code (13) plus 4 sub-fields.
            AddSection(
                consignes, source.OF, lingot.CodeOperation, source.CodeConsigneLingot,
                (15, raw => raw.Substring(0, 3)),
                (7, raw => raw.Substring(4, 1)),
                (8, raw => raw.Substring(6, 3)),
                (9, raw => raw.Substring(10, 2)));
        }

        if (pits is not null)
        {
            // PC1 (OrdreDeFabricationManager.cs:1452-1482): full code (13) plus 5 sub-fields. Types 10 and
            // 11 read the identical Substring(2,3) slice - legacy's exceptTypeConsigne exclusion parameter
            // (guarding 10/11 individually) has no caller-supplied value anywhere in the P60 dispatch path
            // (Design Notes, spec-4-4-bis), so both always fire from the same slice.
            AddSection(
                consignes, source.OF, pits.CodeOperation, source.CodeConsignePits,
                (12, raw => raw.Substring(0, 1)),
                (10, raw => raw.Substring(2, 3)),
                (11, raw => raw.Substring(2, 3)),
                (5, raw => raw.Substring(6, 2).Trim()),
                (6, raw => raw.Substring(9, 3).Trim()));
        }

        if (poidsMetrique is not null)
        {
            // XP9 (OrdreDeFabricationManager.cs:1612-1636): full code (13) plus 3 sub-fields.
            AddSection(
                consignes, source.OF, poidsMetrique.CodeOperation, source.CodeConsignePoidMetrique,
                (18, raw => raw.Substring(0, 4).Trim()),
                (19, raw => raw.Substring(5, 2).Trim()),
                (20, raw => raw.Substring(8, 2).Trim()));
        }

        if (refroidissoirs is not null)
        {
            // XA1 (OrdreDeFabricationManager.cs:1642-1664): full code (13) plus 3 sub-fields.
            AddSection(
                consignes, source.OF, refroidissoirs.CodeOperation, source.CodeConsigneRefroidissoir,
                (21, raw => raw.Substring(0, 2).Trim()),
                (22, raw => raw.Substring(3, 1).Trim()),
                (23, raw => raw.Substring(5, 3).Trim()));
        }

        // Story 4.12 (AC-FR19-5): every row gets its label from the pure port of the legacy
        // LibelleConsigneController.GetLibelle, over the reference snapshot the caller loaded. With no
        // snapshot it is empty, so a lookup-based label becomes "?" and a computed one is still produced.
        // Type 22 also reads the Ordre de Fabrication's ProfilProduit and DiametreProduit and the Pits
        // H2Coulee, the legacy call-site arguments (OrdreDeFabricationManager.cs:1405-1416).
        ConsigneReferenceData reference = referenceData ?? ConsigneReferenceData.Empty;
        string? Libelle(L_D_CONSIGNES consigne) => LibelleConsigneResolver.Resolve(
            consigne.CodeOperation,
            consigne.TypeConsigne,
            consigne.CodeConsigne,
            reference,
            ordreFabrication?.ProfilProduit,
            ordreFabrication?.DiametreProduit,
            pits?.H2Coulee).Libelle;

        foreach (L_D_CONSIGNES consigne in consignes)
        {
            consigne.LibelleConsigne = Libelle(consigne);
        }

        // Story 4.13 (AC-FR19-6): one working copy per decoded row, then the composite labels of the legacy
        // BuildLibelleConsigne, called once per section after all its rows exist. Only the working copy
        // receives them, because GetConsignes defaults to gpao=false (OrdreFabrication.cs:60), so the
        // ConsigneGPAO=true rows 13 and 24 keep "?".
        List<L_D_CONSIGNES> working = [.. consignes.Select(WorkingCopy)];
        if (chutage is not null)
        {
            SetLabel(SectionRows(working, chutage.CodeOperation), 13, LibelleConsigneComposer.Chutage);
        }

        if (decoupe is not null)
        {
            List<L_D_CONSIGNES> rows = SectionRows(working, decoupe.CodeOperation);
            (string sizeTwelve, string sizeEighteen) = LibelleConsigneComposer.Decoupe(rows);
            SetLabel(rows, 13, _ => sizeTwelve);
            SetLabel(rows, 24, _ => sizeEighteen);
        }

        if (lingot is not null)
        {
            SetLabel(SectionRows(working, lingot.CodeOperation), 13, LibelleConsigneComposer.Lingot);
        }

        if (pits is not null)
        {
            SetLabel(SectionRows(working, pits.CodeOperation), 13, LibelleConsigneComposer.Pits);
        }

        if (poidsMetrique is not null)
        {
            SetLabel(SectionRows(working, poidsMetrique.CodeOperation), 13, LibelleConsigneComposer.PoidsMetrique);
        }

        if (refroidissoirs is not null)
        {
            SetLabel(SectionRows(working, refroidissoirs.CodeOperation), 13, LibelleConsigneComposer.Refroidissoirs);
        }

        if (svt is not null)
        {
            // SVT: legacy has no decode rule for this section (OrdreDeFabricationManager.cs:1668-1669, a
            // dead, commented-out read) - unchanged single ConsigneGPAO=true row, added after the working
            // copies were taken because it gets none: CompleteConsignes2 never touches SVT (Story 4.13).
            // TypeConsigne, SizeCodeConsigne: assumed, unverified - à_clarifier per the Story 4.2 annex, left
            // at their CLR default (see deferred-work.md, "Deferred from: story-4.4-bis decomposition of
            // L_D_CONSIGNES", for the collision risk this leaves open).
            L_D_CONSIGNES svtRow = Row(source.OF, svt.CodeOperation, source.CodeConsigneSVT ?? string.Empty, typeConsigne: 0, sizeCodeConsigne: 0);
            svtRow.LibelleConsigne = Libelle(svtRow);
            consignes.Add(svtRow);
        }

        consignes.AddRange(working);
        return consignes;
    }

    // The working-copy rows of one section, which all share its CodeOperation.
    private static List<L_D_CONSIGNES> SectionRows(List<L_D_CONSIGNES> working, string codeOperation) =>
        [.. working.Where(row => row.CodeOperation == codeOperation)];

    // Writes the composite on the section's row of that type, computed before the write. A missing row, or
    // a null composite (the legacy exception that skips the assignment), leaves the rows unchanged.
    private static void SetLabel(List<L_D_CONSIGNES> rows, int typeConsigne, Func<IReadOnlyList<L_D_CONSIGNES>, string?> composer)
    {
        string? libelle = composer(rows);
        L_D_CONSIGNES? row = rows.Find(r => r.TypeConsigne == typeConsigne);
        if (row is not null && libelle is not null)
        {
            row.LibelleConsigne = libelle;
        }
    }

    private static L_D_CONSIGNES WorkingCopy(L_D_CONSIGNES row) => new()
    {
        CodeConsigne = row.CodeConsigne,
        CodeOperation = row.CodeOperation,
        ConsigneGPAO = false,
        LibelleConsigne = row.LibelleConsigne,
        OF = row.OF,
        SizeCodeConsigne = row.SizeCodeConsigne,
        TypeConsigne = row.TypeConsigne,
    };

    // Shared shape for the 5 sections whose full code is always TypeConsigne=13/size 12: skip entirely
    // (no rows at all, matching legacy's own guard) when the section's own raw consigne code is blank,
    // otherwise emit the full-code row plus one row per (TypeConsigne, slice) sub-field. The slice
    // functions read a 12-character-padded copy of the raw code (see PadForSlicing) so a Champ the XML
    // normalization already right-trimmed of its trailing space padding cannot make a legacy offset run
    // past the end of the string; the full-code row itself still carries the untouched raw value.
    private static void AddSection(
        List<L_D_CONSIGNES> consignes,
        string of,
        string codeOperation,
        string? rawCode,
        params (int TypeConsigne, Func<string, string> Slice)[] subFields)
    {
        if (string.IsNullOrEmpty(rawCode))
        {
            return;
        }

        consignes.Add(Row(of, codeOperation, rawCode, typeConsigne: 13, sizeCodeConsigne: 12));

        string padded = PadForSlicing(rawCode, 12);
        foreach ((int typeConsigne, Func<string, string> slice) in subFields)
        {
            consignes.Add(Row(of, codeOperation, slice(padded), typeConsigne, sizeCodeConsigne: 12));
        }
    }

    // XP1 (OrdreDeFabricationManager.cs:1557-1599): two independent raw codes decode this section - the
    // size-12 code (13/16/17, from CodeConsigneDecoupe) and the size-18 code (24/25-29, from
    // LibelleConsigneDecoupe, Templates/P60.xml Position 287, Size 18). Despite its Champ name, that field
    // is the second Decoupe consigne: legacy turns the second consigne it adds to this section while
    // loading a KAPE22 into TypeConsigne 24/size 18 (OrdreFabrication.cs:627-634), and every real P60
    // fixture carries a structured code there (e.g. ".05750 BC 2.7M" - optimised length, short bar, short
    // bar length), never a free-text label like the other sections' Libelle Champs. Each block is gated
    // on its own code being non-blank, independently of the other (legacy line 1577).
    private static void AddDecoupe(List<L_D_CONSIGNES> consignes, string of, string codeOperation, string? sizeTwelveCode, string? sizeEighteenCode)
    {
        if (!string.IsNullOrEmpty(sizeTwelveCode))
        {
            consignes.Add(Row(of, codeOperation, sizeTwelveCode, typeConsigne: 13, sizeCodeConsigne: 12));

            string padded12 = PadForSlicing(sizeTwelveCode, 12);
            consignes.Add(Row(of, codeOperation, padded12.Substring(0, 5).Trim(), 16, 12));
            consignes.Add(Row(of, codeOperation, padded12.Substring(6, 5).Trim(), 17, 12));
        }

        if (string.IsNullOrEmpty(sizeEighteenCode))
        {
            return;
        }

        string padded18 = PadForSlicing(sizeEighteenCode, 18);
        consignes.Add(Row(of, codeOperation, sizeEighteenCode, 24, 18));
        consignes.Add(Row(of, codeOperation, padded18.Substring(0, 1).Trim(), 25, 18));
        consignes.Add(Row(of, codeOperation, padded18.Substring(1, 5).Trim(), 26, 18));
        consignes.Add(Row(of, codeOperation, padded18.Substring(7, 2).Trim(), 27, 18));
        // TypeConsigne 28 (Substring(9,4), indices 9-12) and 29 (Substring(11,1), index 11) deliberately
        // overlap by one character (index 11) - a faithful reproduction of legacy's own overlapping
        // offsets (OrdreDeFabricationManager.cs:1592,1596), not a transcription error.
        consignes.Add(Row(of, codeOperation, padded18.Substring(9, 4).Trim(), 28, 18));
        consignes.Add(Row(of, codeOperation, padded18.Substring(11, 1).Trim(), 29, 18));
    }

    // Right-pads to at least the wire format's declared Champ width, a no-op when the value already
    // reaches it - only ever widens a value the XML layer has right-trimmed, never truncates one.
    private static string PadForSlicing(string rawCode, int width) => rawCode.Length >= width ? rawCode : rawCode.PadRight(width);

    private static L_D_CONSIGNES Row(string of, string codeOperation, string codeConsigne, int typeConsigne, int sizeCodeConsigne) => new()
    {
        CodeConsigne = codeConsigne,
        CodeOperation = codeOperation,
        ConsigneGPAO = true,
        OF = DownstreamOf.Pad(of),
        SizeCodeConsigne = sizeCodeConsigne,
        TypeConsigne = typeConsigne,
    };
}
