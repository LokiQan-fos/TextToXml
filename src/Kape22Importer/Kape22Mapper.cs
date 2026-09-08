using System;
using System.Collections.Generic;
using System.Reflection;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 2.4: turns the Kape22File DTO's Detail block (Kape22FileMessage) into an insertable
// L_D_KAPE22 entity (PRD Annexe B). Default rule: copy by case-insensitive property name (types are
// already aligned, checked at worker startup by FR-8). NamingExceptions and IgnoredProperties are the
// only two deviations from that default, built once like P60Deserializer's Lazy<> setup.
// The P60-specific work sits in two collaborators (Epic 3 story-0 hygiene, retro action A-1 volet b):
// CoherenceChecker for the FR-10 non-blocking Warnings, DerivedFields for the FR-9 rules (day-of-year
// Header Date D4, roulette NumeroFichier, worker DateReception). The clock is constructor-injected so
// those derived timestamps are deterministic under test (AR-12); production leaves it null.
public sealed class Kape22Mapper(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

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

    // sourceFileName feeds the FR-10 file-name coherence check (AC-FR10-4, AC-FR10-5).
    public MapResult<L_D_KAPE22> Map(string normalizedXml, string sourceFileName)
    {
        P60DeserializeResult deserialized = P60Deserializer.Deserialize(normalizedXml);
        if (deserialized.File is null)
        {
            return new MapResult<L_D_KAPE22> { Errors = deserialized.Errors };
        }

        Kape22File file = deserialized.File;
        List<ConversionError> warnings = CoherenceChecker.Check(file, sourceFileName);
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
            try
            {
                target.SetValue(entity, value);
            }
            catch (ArgumentException error)
            {
                // FR-8's startup check (StartupCompatibilityCheck.Verify) rejects a Descripteur whose
                // datatype does not fit its L_D_KAPE22 column before any Fichier is processed. If a
                // mismatch still reaches this reflective assignment - the check was not wired, or a path
                // it does not cover - surface it as the same deployment-fault type instead of a raw
                // reflection exception leaving Map.
                throw new StartupCompatibilityException(
                    $"Champ '{source.Name}' produced a {value?.GetType().Name ?? "null"} value that "
                    + $"column {target.Name} ({target.PropertyType.Name}) cannot accept; the FR-8 "
                    + "compatibility check should have caught this at startup.",
                    error);
            }
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

        // FR-9 (D4): DerivedFields stamps NumeroFichier and DateReception on the entity and rejects a
        // blank Header roulette or an invalid day-of-year Date.
        errors.AddRange(new DerivedFields(this.clock).Apply(file, entity));

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
