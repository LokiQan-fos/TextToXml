using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TextToXml;

namespace Kape22Importer;

// Story 2.7 (FR-10): the three non-blocking coherence checks Kape22Mapper.Map runs over a deserialized
// Fichier (§0bis D16). Each divergence is one Warning; none of them stops the Fichier from being
// mapped or inserted. Extracted from Kape22Mapper (Epic 3 story-0 hygiene, retro action A-1 volet b).
public static class CoherenceChecker
{
    // The Footer is the third and last Ligne of a P60 Fichier (D3).
    private const int FooterLineNumber = 3;

    // Footer.Records must count exactly Entete + message + Pied (§0bis D18).
    private const int ExpectedRecordCount = 3;

    // A P60 Fichier name decomposes as File_Emet_Recepteur_NumeroFichier: four segments, three
    // separators (AC-FR10-4).
    private const int FileNameSegmentCount = 4;

    // AC-FR10-1 / AC-FR10-3 / AC-FR10-4 / AC-FR10-5: runs in order Footer.Records, then the inter-Bloc
    // File Champ, then the file-name segments.
    public static List<ConversionError> Check(Kape22File file, string sourceFileName)
    {
        ArgumentNullException.ThrowIfNull(file);

        List<ConversionError> warnings = [];

        // AC-FR10-1 (D18): Footer.Records must be exactly 3. A blank or non-numeric Champ is still
        // "not 3", so it is a Warning here, never a blocking typing error (the Champ is datatype
        // "string" in the Descripteur and is not validated in Step 1).
        if (!int.TryParse(file.Footer.Records.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int records)
            || records != ExpectedRecordCount)
        {
            warnings.Add(new ConversionError
            {
                Block = Block.Footer,
                Code = ErrorCode.InterBlockMismatch,
                FieldId = "Records",
                LineNumber = FooterLineNumber,
                Message = $"Le Champ 'Records' du Pied ('{file.Footer.Records}') ne vaut pas {ExpectedRecordCount} (Entête + message + Pied).",
                RawValue = file.Footer.Records,
            });
        }

        // AC-FR10-3: the File Champ (Position 0, Size 3) is present in the three Blocs and must agree.
        // Trimmed before the comparison, like the file-name segment check, so space padding on a single
        // Bloc is not read as a divergence.
        string[] fileChamps = [file.Header.File.Trim(), file.Message.File.Trim(), file.Footer.File.Trim()];
        if (fileChamps.Distinct(StringComparer.Ordinal).Count() > 1)
        {
            warnings.Add(new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.InterBlockMismatch,
                FieldId = "File",
                Message = $"Le Champ 'File' diffère entre les Blocs : Entête '{file.Header.File}', Détail '{file.Message.File}', Pied '{file.Footer.File}'.",
            });
        }

        warnings.AddRange(CheckFileName(file.Header, sourceFileName));

        return warnings;
    }

    // AC-FR10-4 / AC-FR10-5: the Fichier name decomposes as File_Emet_Recepteur_NumeroFichier. A name
    // outside that pattern (not exactly three separators, once any extension is dropped) is a single
    // FileNameMismatch citing the name; otherwise each segment differing from its Entête homonym is
    // its own FileNameMismatch. Leading zeros are ignored for NumeroFichier only.
    private static IEnumerable<ConversionError> CheckFileName(Kape22FileHeader header, string sourceFileName)
    {
        string bareName = Path.GetFileNameWithoutExtension(sourceFileName);
        string[] segments = bareName.Split('_');
        if (segments.Length != FileNameSegmentCount)
        {
            return
            [
                new ConversionError
                {
                    Block = Block.File,
                    Code = ErrorCode.FileNameMismatch,
                    Message = $"Le nom de fichier '{bareName}' ne suit pas le motif attendu 'File_Emet_Recepteur_NumeroFichier'.",
                    RawValue = bareName,
                },
            ];
        }

        // Descripteur Champ Id -> the name segment expected to match it, in name order.
        (string FieldId, string Segment, string Expected)[] pairs =
        [
            ("File", segments[0], header.File),
            ("Emet", segments[1], header.Emet),
            ("Recepteur", segments[2], header.Recepteur),
            ("NumeroFichier", segments[3], header.NumeroFichier),
        ];

        List<ConversionError> mismatches = [];
        foreach ((string fieldId, string segment, string expected) in pairs)
        {
            bool matches = fieldId == "NumeroFichier"
                ? SameNumber(segment, expected)
                : string.Equals(segment.Trim(), expected.Trim(), StringComparison.Ordinal);
            if (matches)
            {
                continue;
            }

            mismatches.Add(new ConversionError
            {
                Block = Block.File,
                Code = ErrorCode.FileNameMismatch,
                FieldId = fieldId,
                Message = $"Le segment '{segment}' du nom de fichier ne correspond pas au Champ '{fieldId}' de l'Entête ('{expected}').",
                RawValue = segment,
            });
        }

        return mismatches;
    }

    // True when both operands are integers of equal value (leading zeros ignored), or equal as trimmed
    // text when either is not numeric.
    private static bool SameNumber(string left, string right) =>
        int.TryParse(left.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber)
        && int.TryParse(right.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber)
            ? leftNumber == rightNumber
            : string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}
