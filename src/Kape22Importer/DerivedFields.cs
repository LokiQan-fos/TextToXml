using System;
using System.Collections.Generic;
using System.Globalization;
using Kape22Importer.Persistence;
using TextToXml;

namespace Kape22Importer;

// Story 2.6 (FR-9): the P60-specific derived rules Kape22Mapper.Map applies on top of the Annexe B
// property copy - the worker DateReception timestamp (AC-FR9-3), the Header roulette NumeroFichier
// (AC-FR9-2), and the day-of-year Header Date validity (AC-FR9-1, D4). Extracted from Kape22Mapper
// (Epic 3 story-0 hygiene, retro action A-1 volet b). The clock is injected so the derived timestamps
// are deterministic under test (AR-12).
public sealed class DerivedFields(TimeProvider timeProvider)
{
    // A P60 Fichier is Header + Detail + Footer, so the Header Bloc is always the first Ligne (D3).
    private const int HeaderLineNumber = 1;

    // Stamps the derived L_D_KAPE22 columns on entity and returns one ConversionError per derived rule
    // the Fichier fails (a blank Header roulette, an invalid day-of-year Date).
    public IReadOnlyList<ConversionError> Apply(Kape22File file, L_D_KAPE22 entity)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(entity);

        // AC-FR9-2: NumeroFichier is the Header roulette Champ, not the Detail homonym (which is ignored).
        entity.NumeroFichier = file.Header.NumeroFichier;

        // AC-FR9-3: DateReception is the worker processing timestamp, in Paris local time.
        entity.DateReception = Reception();

        List<ConversionError> errors = [];

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

        // AC-FR9-1 (D4): the Header Date is a day-of-year number in the current Paris year. Its
        // converted value has no L_D_KAPE22 column in Epic 2, so only its validity is enforced here;
        // "000", a non-numeric Champ, or a day past the length of that year rejects the Fichier.
        if (!TryConvertHeaderDate(file.Header.Date, out _))
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

        return errors;
    }

    // The current instant in Paris local time (D4, AC-FR9-3), from the injected clock.
    private DateTime Reception() =>
        TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, ParisTime.Zone);

    // AC-FR9-1 (D4): interprets a Header Date Champ as a day-of-year number in the current year, Paris
    // time. Surrounding whitespace is tolerated (fixed-width source fields may be space-padded).
    // Returns false for a non-numeric Champ, "000", or a number past the length of the current Paris
    // year (365, or 366 in a leap year), so a converted date never leaves that year.
    public bool TryConvertHeaderDate(string raw, out DateTime date)
    {
        ArgumentNullException.ThrowIfNull(raw);

        date = default;
        if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int dayOfYear))
        {
            return false;
        }

        int year = Reception().Year;
        int daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        if (dayOfYear < 1 || dayOfYear > daysInYear)
        {
            return false;
        }

        date = new DateTime(year, 1, 1).AddDays(dayOfYear - 1);
        return true;
    }
}
