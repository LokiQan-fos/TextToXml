using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TextToXml.Tests;

namespace Kape22Importer.Tests;

// One column of a CREATE TABLE, parsed out of a scripts/schema/*.sql file. SqlType is the raw
// upper-cased type token (for example "DATETIME"), kept so the parity test can catch store-type drift.
internal sealed record SqlColumn(Type ClrType, bool IsNullable, string Name, string SqlType);

// Minimal reader for the generated scripts/schema/*.sql files, used only to lock the EF model against
// the real schema (risk R-3). It understands just the subset those generated files use: one column per
// line, "[Name] TYPE[(len)] NULL|NOT NULL", plus a trailing CONSTRAINT line it ignores.
internal static class SqlTableSchema
{
    private static readonly Regex TableBlock = new(
        @"CREATE\s+TABLE\s+dbo\.(?<name>\w+)\s*\((?<body>.*?)\)\s*;",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ColumnLine = new(
        @"^\s*\[(?<name>\w+)\]\s+(?<type>\w+)(?:\s*\([^)]*\))?(?:\s+IDENTITY\s*\([^)]*\))?\s+(?<nullability>NOT\s+NULL|NULL)\s*,?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<SqlColumn> Read(string scriptFileName, string tableName)
    {
        string path = RepoLayout.ProjectFile(Path.Combine("scripts", "schema", scriptFileName));
        string sql = File.ReadAllText(path);

        Match table = TableBlock.Matches(sql)
            .FirstOrDefault(match => string.Equals(match.Groups["name"].Value, tableName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Table dbo.{tableName} not found in {scriptFileName}.");

        List<SqlColumn> columns = [];
        foreach (string rawLine in table.Groups["body"].Value.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match column = ColumnLine.Match(line);
            if (!column.Success)
            {
                throw new InvalidOperationException($"Unparsed column line in {scriptFileName}: '{line}'.");
            }

            bool isNullable = !column.Groups["nullability"].Value.Replace(" ", string.Empty)
                .Equals("NOTNULL", StringComparison.OrdinalIgnoreCase);

            string sqlType = column.Groups["type"].Value.ToUpperInvariant();
            columns.Add(new SqlColumn(ClrTypeFor(sqlType), isNullable, column.Groups["name"].Value, sqlType));
        }

        return columns;
    }

    private static Type ClrTypeFor(string sqlType) => sqlType.ToUpperInvariant() switch
    {
        "INT" => typeof(int),
        "BIGINT" => typeof(long),
        "BIT" => typeof(bool),
        "DATETIME" or "DATETIME2" or "DATE" => typeof(DateTime),
        "DECIMAL" or "NUMERIC" or "MONEY" => typeof(decimal),
        "NCHAR" or "NVARCHAR" or "CHAR" or "VARCHAR" or "TEXT" or "NTEXT" => typeof(string),
        _ => throw new InvalidOperationException($"Unmapped SQL type '{sqlType}'."),
    };
}
