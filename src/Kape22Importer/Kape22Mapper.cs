using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 2.4: turns the Kape22File DTO's Detail block (Kape22FileMessage) into an insertable
// L_D_KAPE22 entity (PRD Annexe B). Default rule: copy by case-insensitive property name (types are
// already aligned, checked at worker startup by FR-8). NamingExceptions and IgnoredProperties are the
// only two deviations from that default, built once like P60Deserializer's Lazy<> setup.
// Story 2.6 (FR-9) adds the P60-specific derived rules on top of that copy: the day-of-year Header
// Date (D4), the roulette NumeroFichier from the Header, the worker DateReception timestamp, and the
// blank-Indice rejection; the DateEnfournementFour1/2 slices stay ignored (D14).
public static class Kape22Mapper
{
    // A P60 Fichier is Header + Detail + Footer, so the Header Bloc is always the first Ligne (D3).
    private const int HeaderLineNumber = 1;

    // The Footer is the third and last Ligne of a P60 Fichier (D3).
    private const int FooterLineNumber = 3;

    // Footer.Records must count exactly Entete + message + Pied (§0bis D18).
    private const int ExpectedRecordCount = 3;

    // A P60 Fichier name decomposes as File_Emet_Recepteur_NumeroFichier: four segments, three
    // separators (AC-FR10-4).
    private const int FileNameSegmentCount = 4;

    // The derived rules interpret and stamp times in Paris local time (D4 for the Header Date,
    // AC-FR9-3 for DateReception). Resolved once; .NET maps this IANA id to the Windows zone on
    // Windows too.
    private static readonly TimeZoneInfo ParisTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    // Annexe B's one naming exception: DTO property name -> L_D_KAPE22 property name.
    public static readonly IReadOnlyDictionary<string, string> NamingExceptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OFOriginInterne"] = "OForiginInterne",
        };

    // Annexe B's ignored DTO properties: envelope/reserved fields and the Four1/Four2 date+heure
    // pairs (derived elsewhere, §0bis D14), which have no direct L_D_KAPE22 column.
    public static readonly IReadOnlySet<string> IgnoredProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Kape22FileMessage.File),
        nameof(Kape22FileMessage.Date),
        nameof(Kape22FileMessage.NumeroFichier),
        nameof(Kape22FileMessage.Segment),
        nameof(Kape22FileMessage.Element),
        nameof(Kape22FileMessage.KAP),
        nameof(Kape22FileMessage.DateEnfournementFour1_Date),
        nameof(Kape22FileMessage.DateEnfournementFour1_Heure),
        nameof(Kape22FileMessage.DateEnfournementFour2_Date),
        nameof(Kape22FileMessage.DateEnfournementFour2_Heure),
    };

    // Annexe B "Legacy blank-Champ defaults": the two L_D_KAPE22 columns whose value for a blank
    // Champ is a legacy default rather than a verbatim copy (see Map). Exposed so the AC-FR7-2
    // completeness sweep skips them; the fixup itself is the two explicit assignments in Map. The
    // general "blank int Champ -> 0" rule (D14b) needs no entry - it lives in DefaultForNonNullable.
    // Names in case-insensitive dictionary order (CC-4).
    public static readonly IReadOnlySet<string> LegacyBlankFillColumns = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(L_D_KAPE22.AcompteSolde),
        nameof(L_D_KAPE22.OForiginInterne),
    };

    // Any Champ Id starting with "Reserve" is ignored too (ReserveRefroidissoir, ReserveRefroidissoir2/3).
    public static bool IsIgnored(string dtoPropertyName) =>
        IgnoredProperties.Contains(dtoPropertyName)
        || dtoPropertyName.StartsWith("Reserve", StringComparison.OrdinalIgnoreCase);

    // Applies Annexe B's naming exception, if any, else falls back to the DTO property name.
    public static string ResolveTargetName(string dtoPropertyName) =>
        NamingExceptions.TryGetValue(dtoPropertyName, out string? renamed) ? renamed : dtoPropertyName;

    // sourceFileName feeds the FR-10 file-name coherence check (AC-FR10-4, AC-FR10-5). The
    // timeProvider parameter is injectable for testing (AC-FR9-1, AC-FR9-3); production passes
    // TimeProvider.System.
    public static MapResult<L_D_KAPE22> Map(string normalizedXml, string sourceFileName, TimeProvider? timeProvider = null)
    {
        timeProvider ??= TimeProvider.System;

        P60DeserializeResult deserialized = P60Deserializer.Deserialize(normalizedXml);
        if (deserialized.File is null)
        {
            return new MapResult<L_D_KAPE22> { Errors = deserialized.Errors };
        }

        Kape22File file = deserialized.File;
        List<ConversionError> warnings = CheckCoherence(file, sourceFileName);
        L_D_KAPE22 entity = new();
        Kape22FileMessage message = file.Message;

        foreach (PropertyInfo source in typeof(Kape22FileMessage).GetProperties())
        {
            if (IsIgnored(source.Name))
            {
                continue;
            }

            string targetName = ResolveTargetName(source.Name);
            PropertyInfo target = typeof(L_D_KAPE22).GetProperty(
                targetName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;

            object? value = source.GetValue(message) ?? DefaultForNonNullable(target.PropertyType);
            target.SetValue(entity, value);
        }

        // Annexe B "Legacy blank-Champ defaults": two Champs the legacy import fills with a fixed
        // value instead of copying it, confirmed against the production L_D_KAPE22 (parity check
        // 2026-09-07). OForiginInterne is the one string column it leaves NULL; AcompteSolde it
        // always writes 'S'. A populated Champ is copied verbatim by the loop above (assumed,
        // unverified - the legacy import source is unavailable, see deferred-work.md).
        if (string.IsNullOrWhiteSpace(message.OForiginInterne))
        {
            entity.OForiginInterne = null;
        }

        if (string.IsNullOrWhiteSpace(message.AcompteSolde))
        {
            entity.AcompteSolde = "S";
        }

        List<ConversionError> errors = [];

        // AC-FR9-2: NumeroFichier is the Header roulette Champ, not the Detail homonym (which is ignored).
        entity.NumeroFichier = file.Header.NumeroFichier;

        // AC-FR9-2: the roulette feeds the NOT NULL NumeroFichier column and is half the D22
        // anti-duplicate key, so a blank Header roulette rejects the Fichier here. RequiredFieldCheck
        // skips it as a derived column, so the check lives with the derivation.
        if (string.IsNullOrWhiteSpace(file.Header.NumeroFichier))
        {
            errors.Add(new ConversionError
            {
                Block = Block.Header,
                Code = ErrorCode.RequiredFieldMissing,
                Column = nameof(L_D_KAPE22.NumeroFichier),
                FieldId = "NumeroFichier",
                LineNumber = HeaderLineNumber,
                Message = "Le Champ obligatoire 'NumeroFichier' de l'Entête est vide (colonne NumeroFichier NOT NULL).",
            });
        }

        // AC-FR9-3: DateReception is the worker processing timestamp, in Paris local time.
        entity.DateReception = ParisNow(timeProvider);

        // AC-FR9-1 (D4): the Header Date is a day-of-year number in the current Paris year. Its
        // converted value has no L_D_KAPE22 column in Epic 2, so only its validity is enforced here;
        // "000", a non-numeric Champ, or a day past the length of that year rejects the Fichier.
        if (!TryConvertHeaderDate(file.Header.Date, timeProvider, out _))
        {
            errors.Add(new ConversionError
            {
                Block = Block.Header,
                Code = ErrorCode.InvalidDate,
                FieldId = "Date",
                LineNumber = HeaderLineNumber,
                Message = $"Le Champ 'Date' de l'Entête ('{file.Header.Date}') n'est pas un numéro de jour valide pour l'année courante.",
                RawValue = file.Header.Date,
            });
        }

        // AC-FR9-4 and the per-file half of FR-8: a NOT NULL column left blank by Step 1 (notably a
        // missing Detail Indice) is a rejected Fichier.
        errors.AddRange(RequiredFieldCheck.Check(file));

        // AC-FR11-4 / D22: the Header roulette and the trimmed Detail OF ride along even on a rejection,
        // so the persister can write the REJETÉ L_D_LOG_COMMANDE line and key the anti-duplicate guard.
        string numeroFichier = file.Header.NumeroFichier;
        string of = file.Message.OF.Trim();

        return errors.Count > 0
            ? new MapResult<L_D_KAPE22> { Errors = errors, NumeroFichier = numeroFichier, OF = of, Warnings = warnings }
            : new MapResult<L_D_KAPE22> { NumeroFichier = numeroFichier, OF = of, Value = entity, Warnings = warnings };
    }

    // AC-FR10-1 / AC-FR10-3 / AC-FR10-4 / AC-FR10-5 (§0bis D16): the three non-blocking coherence
    // checks. Each divergence is one Warning; none of them stops the Fichier from being mapped or
    // inserted. Order: Footer.Records, then the inter-Bloc File Champ, then the file-name segments.
    public static List<ConversionError> CheckCoherence(Kape22File file, string sourceFileName)
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

    // AC-FR9-1 (D4): interprets a Header Date Champ as a day-of-year number in the current year, Paris
    // time. Surrounding whitespace is tolerated (fixed-width source fields may be space-padded).
    // Returns false for a non-numeric Champ, "000", or a number past the length of the current Paris
    // year (365, or 366 in a leap year), so a converted date never leaves that year.
    public static bool TryConvertHeaderDate(string raw, TimeProvider timeProvider, out DateTime date)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        date = default;
        if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int dayOfYear))
        {
            return false;
        }

        int year = ParisNow(timeProvider).Year;
        int daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        if (dayOfYear < 1 || dayOfYear > daysInYear)
        {
            return false;
        }

        date = new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
        return true;
    }

    // The current instant in Paris local time, from the injected clock.
    private static DateTime ParisNow(TimeProvider timeProvider) =>
        TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, ParisTimeZone);

    // The value a blank Champ takes on its L_D_KAPE22 column. An integer column (nullable or not)
    // takes 0, never NULL: the legacy import zero-filled every blank int Champ (Annexe B "Legacy
    // blank-Champ defaults" / production parity 2026-09-07 - no NULL in any L_D_KAPE22 int column
    // over 17710 rows). Any other non-nullable value type keeps its own default (e.g. the NOT NULL
    // Indice); every other nullable target stays null (a blank string keeps its empty value).
    public static object? DefaultForNonNullable(Type targetType)
    {
        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlying == typeof(int))
        {
            return 0;
        }

        return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
            ? Activator.CreateInstance(targetType)
            : null;
    }
}

// Outcome of Kape22Mapper.Map. Mirrors ConversionResult/P60DeserializeResult: Success is true exactly
// when Errors is empty; Value is null on failure. Warnings are the non-blocking coherence signals of
// FR-10 (D16) and never influence Success.
// NumeroFichier (Header roulette) and OF (trimmed Detail Champ) are exposed even on a mapping failure,
// as long as deserialization succeeded, so the Story 2.8 persister can write the "REJETÉ"
// L_D_LOG_COMMANDE line (AC-FR11-4). Both stay null when deserialization itself failed, which is the
// D15 "OF unreadable" path where no L_D_LOG_COMMANDE row is written.
// Properties are declared in alphabetical order (CC-4).
public sealed record MapResult<T>
{
    public IReadOnlyList<ConversionError> Errors { get; init; } = [];

    public string? NumeroFichier { get; init; }

    public string? OF { get; init; }

    public bool Success => Errors.Count == 0;

    public T? Value { get; init; }

    public IReadOnlyList<ConversionError> Warnings { get; init; } = [];
}
