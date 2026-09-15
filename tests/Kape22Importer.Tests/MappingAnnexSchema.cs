using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kape22Importer.Tests;

// One row of the Story 4.2 mapping annex: a target column plus how it derives from L_D_KAPE22 (or from a
// business rule). Table/Column identify the target; Status must be one of MappingAnnexStatus's three
// values (checked by MappingAnnexCompleteness, not here). Properties are declared in alphabetical order
// (CC-4).
internal sealed record MappingAnnexEntry(string Column, string SourceOrRule, string Status, string Table);

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
// "| Colonne | Statut | Source / Règle |" Markdown table. Mirrors SqlTableSchema's role for the SQL
// schema files - a thin, throw-on-drift parser, not a general Markdown engine.
internal static class MappingAnnex
{
    private static readonly Regex TableHeading = new(
        @"^###\s+(?<table>\S+)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Row = new(
        @"^\|\s*(?<column>[^|]+?)\s*\|\s*(?<status>[^|]+?)\s*\|\s*(?<rule>[^|]+?)\s*\|\s*$",
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

            // A line starting with "|" that the 3-cell shape doesn't match is drift (e.g. a literal "|"
            // inside a cell) - fail loudly instead of silently misaligning columns.
            if (!row.Success)
            {
                throw new FormatException($"Malformed annex row under section '{table}': {line}");
            }

            string column = row.Groups["column"].Value;

            // Skip the header row and the "| --- | --- | --- |" separator row, plain or colon-aligned.
            if (column.Equals("Colonne", StringComparison.Ordinal) || column.Trim(':').StartsWith('-'))
            {
                continue;
            }

            yield return new MappingAnnexEntry(column, row.Groups["rule"].Value, row.Groups["status"].Value, table);
        }
    }
}

// AC-FR17-5: confronts the Story 4.2 annex against the Story 4.1 EF model. Fails on a model column with
// no annex row (a true hole), an annex row naming a table/column absent from the model (an orphan entry -
// a rename or a typo), an annex row whose Status is not one of the three allowed values, and an
// "à_clarifier" row not cited by deferred-work.md (a literal "Table.Column" match) - mechanising the
// "assumed, unverified" pattern instead of leaving it to human discipline.
internal static class MappingAnnexCompleteness
{
    public static IReadOnlyList<string> Check(
        IReadOnlyList<MappingAnnexEntry> annex,
        IReadOnlyDictionary<string, IReadOnlyList<string>> modelColumnsByTable,
        string deferredWorkContent)
    {
        List<string> failures = [];
        HashSet<(string Table, string Column)> documented = [];

        foreach (MappingAnnexEntry entry in annex)
        {
            if (!modelColumnsByTable.TryGetValue(entry.Table, out IReadOnlyList<string>? columns)
                || !columns.Contains(entry.Column, StringComparer.Ordinal))
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
        }

        foreach ((string table, IReadOnlyList<string> columns) in modelColumnsByTable)
        {
            failures.AddRange(
                columns.Where(column => !documented.Contains((table, column)))
                    .Select(column => $"Missing annex entry: {table}.{column} has no row in the annex."));
        }

        return failures;
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
