using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kape22Importer.Tests;

// One row of the Story 4.2 mapping annex: a target column plus how it derives from L_D_KAPE22 (or from a
// business rule). Table/Column identify the target; Status must be one of MappingAnnexStatus's three
// values (checked by MappingAnnexCompleteness, not here). Scale is the decimal-place count for a
// decimal target column sourced from an int/int? KAPE22 field (Story 4.2-bis, AC-FR17-5 extended); null
// means the annex row carries no Scale ("-"), including every row where Scale does not apply. Properties
// are declared in alphabetical order (CC-4).
internal sealed record MappingAnnexEntry(string Column, int? Scale, string SourceOrRule, string Status, string Table);

// A target-table column plus its CLR property type, used by MappingAnnexCompleteness to check the
// decimal-from-int-without-scale rule against the real EF model. Properties are declared in alphabetical
// order (CC-4).
internal sealed record ModelColumn(Type ClrType, string Name);

// The three Status values the Story 4.2 annex allows (epics.md, AC-FR17-1), reused verbatim from the
// Épic 2 production-parity vocabulary (spec-parity-kape22-legacy-fill-rules.md).
internal static class MappingAnnexStatus
{
    public const string Rule = "règle";

    public const string Sourced = "sourcée";

    public const string ToClarify = "à_clarifier";
}

// Minimal reader for the Story 4.2 annex (_bmad-output/implementation-artifacts/
// annexe-mapping-dispatch-epic4.md): one "### TableName" heading per target table, followed by a
// "| Colonne | Statut | Source / Règle | Scale |" Markdown table (Scale added Story 4.2-bis). Mirrors
// SqlTableSchema's role for the SQL schema files - a thin, throw-on-drift parser, not a general Markdown
// engine.
internal static class MappingAnnex
{
    private static readonly Regex TableHeading = new(
        @"^###\s+(?<table>\S+)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Row = new(
        @"^\|\s*(?<column>[^|]+?)\s*\|\s*(?<status>[^|]+?)\s*\|\s*(?<rule>[^|]+?)\s*\|\s*(?<scale>[^|]+?)\s*\|\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static IReadOnlyList<MappingAnnexEntry> Parse(string markdown)
    {
        List<MappingAnnexEntry> entries = [];
        MatchCollection headings = TableHeading.Matches(markdown);

        for (int i = 0; i < headings.Count; i++)
        {
            string table = headings[i].Groups["table"].Value;
            int start = headings[i].Index + headings[i].Length;
            int end = i + 1 < headings.Count ? headings[i + 1].Index : markdown.Length;

            entries.AddRange(ParseRows(markdown[start..end], table));
        }

        return entries;
    }

    private static IEnumerable<MappingAnnexEntry> ParseRows(string block, string table)
    {
        foreach (string rawLine in block.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Trim();

            if (!line.StartsWith('|'))
            {
                continue;
            }

            Match row = Row.Match(line);

            // A line starting with "|" that the 4-cell shape doesn't match is drift (e.g. a literal "|"
            // inside a cell) - fail loudly instead of silently misaligning columns.
            if (!row.Success)
            {
                throw new FormatException($"Malformed annex row under section '{table}': {line}");
            }

            string column = row.Groups["column"].Value;

            // Skip the header row and the "| --- | --- | --- | --- |" separator row, plain or colon-aligned.
            if (column.Equals("Colonne", StringComparison.Ordinal) || column.Trim(':').StartsWith('-'))
            {
                continue;
            }

            yield return new MappingAnnexEntry(
                column,
                ParseScale(row.Groups["scale"].Value, table, column),
                row.Groups["rule"].Value,
                row.Groups["status"].Value,
                table);
        }
    }

    // "-" (non-applicable) and an empty cell both mean "no Scale"; any other value is the decimal-place
    // count, and "0" is a legitimate value, not a missing one (e.g. DECIMAL(4,0)). A malformed cell fails
    // loudly with the same table/column context as a malformed row, instead of a bare FormatException.
    private static int? ParseScale(string cell, string table, string column)
    {
        string trimmed = cell.Trim();

        if (trimmed.Length == 0 || trimmed == "-")
        {
            return null;
        }

        if (!int.TryParse(trimmed, out int scale))
        {
            throw new FormatException($"Malformed Scale cell for '{table}.{column}': '{cell}'.");
        }

        return scale;
    }
}

// AC-FR17-5: confronts the Story 4.2 annex against the Story 4.1 EF model. Fails on a model column with
// no annex row (a true hole), an annex row naming a table/column absent from the model (an orphan entry -
// a rename or a typo), an annex row whose Status is not one of the three allowed values, an "à_clarifier"
// row not cited by deferred-work.md (a literal "Table.Column" match), and (Story 4.2-bis) a "sourcée" row
// that targets a decimal/decimal? EF column whose KAPE22 source cannot be resolved at all, or resolves to
// an int/int? field with no Scale - mechanising the "assumed, unverified" pattern and the
// int-into-narrow-decimal root cause instead of leaving either to human discipline. A decimal column must
// always cite a resolvable KAPE22 source; silently skipping an unresolvable citation would let a future
// offending column bypass the very guard this story exists to add.
internal static class MappingAnnexCompleteness
{
    // Matches the "KAPE22.<Field>" citation convention used by every "sourcée" row (Design Notes,
    // Story 4.2). A decimal-targeting row with no such citation, or one naming an unknown field, is
    // unresolvable and fails the Scale check rather than passing it silently (Story 4.2-bis).
    private static readonly Regex Kape22FieldCitation = new(@"KAPE22\.(?<field>\w+)", RegexOptions.Compiled);

    public static IReadOnlyList<string> Check(
        IReadOnlyList<MappingAnnexEntry> annex,
        IReadOnlyDictionary<string, IReadOnlyList<ModelColumn>> modelColumnsByTable,
        IReadOnlyDictionary<string, Type> kape22FieldTypesByName,
        string deferredWorkContent)
    {
        List<string> failures = [];
        HashSet<(string Table, string Column)> documented = [];

        foreach (MappingAnnexEntry entry in annex)
        {
            ModelColumn? column = modelColumnsByTable.TryGetValue(entry.Table, out IReadOnlyList<ModelColumn>? columns)
                ? columns.FirstOrDefault(candidate => candidate.Name.Equals(entry.Column, StringComparison.Ordinal))
                : null;

            if (column is null)
            {
                failures.Add($"Orphan annex entry: {entry.Table}.{entry.Column} is not in the EF model.");
                continue;
            }

            documented.Add((entry.Table, entry.Column));

            if (entry.Status is not (MappingAnnexStatus.Sourced or MappingAnnexStatus.Rule or MappingAnnexStatus.ToClarify))
            {
                failures.Add($"{entry.Table}.{entry.Column}: invalid Statut '{entry.Status}'.");
            }
            else if (entry.Status == MappingAnnexStatus.ToClarify
                && !HasGenuineCitation(deferredWorkContent, entry.Table, entry.Column))
            {
                failures.Add($"{entry.Table}.{entry.Column}: marked à_clarifier with no deferred-work.md citation.");
            }
            else if (entry.Status == MappingAnnexStatus.Sourced && IsDecimal(column.ClrType))
            {
                Type? sourceType = SourceKape22Type(entry.SourceOrRule, kape22FieldTypesByName);

                if (sourceType is null)
                {
                    failures.Add($"{entry.Table}.{entry.Column}: cannot resolve KAPE22 source for Scale check.");
                }
                else if (entry.Scale is null && IsInt(sourceType))
                {
                    failures.Add(
                        $"{entry.Table}.{entry.Column}: decimal column sourced from an int KAPE22 field with no Scale.");
                }
            }
        }

        foreach ((string table, IReadOnlyList<ModelColumn> columns) in modelColumnsByTable)
        {
            failures.AddRange(
                columns.Where(column => !documented.Contains((table, column.Name)))
                    .Select(column => $"Missing annex entry: {table}.{column.Name} has no row in the annex."));
        }

        return failures;
    }

    private static bool IsDecimal(Type clrType) => clrType == typeof(decimal) || clrType == typeof(decimal?);

    private static bool IsInt(Type? clrType) => clrType == typeof(int) || clrType == typeof(int?);

    private static Type? SourceKape22Type(string sourceOrRule, IReadOnlyDictionary<string, Type> kape22FieldTypesByName)
    {
        Match match = Kape22FieldCitation.Match(sourceOrRule);

        return match.Success && kape22FieldTypesByName.TryGetValue(match.Groups["field"].Value, out Type? found)
            ? found
            : null;
    }

    // A raw whole-file Contains would accept a "Table.Column" string that only appears by coincidence in
    // an unrelated note (e.g. a different story's review comment). Requiring the "assumed, unverified"
    // marker in the same blank-line-delimited paragraph as the citation keeps the match tied to an actual
    // deferral entry.
    private static bool HasGenuineCitation(string deferredWorkContent, string table, string column)
    {
        string citation = $"{table}.{column}";

        return deferredWorkContent
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None)
            .Any(paragraph =>
                paragraph.Contains(citation, StringComparison.Ordinal)
                && paragraph.Contains("assumed, unverified", StringComparison.Ordinal));
    }
}
