using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TextToXml.Tests;

namespace Kape22Importer.Tests;

// One column of a CREATE TABLE, parsed out of a scripts/schema/*.sql file. SqlType is the raw
// upper-cased type token (for example "DATETIME"), kept so the parity test can catch store-type drift.
// MaxLength is the declared character length of a bounded string column, or null for a non-string
// column and for NVARCHAR(MAX) / NCHAR without a length. DecimalPrecision is the (precision, scale) of a
// DECIMAL(p,s)/NUMERIC(p,s) column (Story 4.14) - null for every other column, and for MONEY, which
// carries no (p,s) token to read. DecimalMagnitude is its exclusive upper bound, 10^(p-s) (Story 4.10,
// B-5), derived from the same parse. Properties are declared in alphabetical order (CC-4).
internal sealed record SqlColumn(Type ClrType, (int Precision, int Scale)? DecimalPrecision, bool IsNullable, int? MaxLength, string Name, string SqlType)
{
    public decimal? DecimalMagnitude => DecimalPrecision is (int precision, int scale) ? (decimal)Math.Pow(10, precision - scale) : null;
}

// Minimal reader for the generated scripts/schema/*.sql files, used only to lock the EF model against
// the real schema (risk R-3). It understands just the subset those generated files use: one column per
// line, "[Name] TYPE[(len)] NULL|NOT NULL", plus a trailing CONSTRAINT line it ignores.
internal static class SqlTableSchema
{
    private static readonly Regex TableBlock = new(
        @"CREATE\s+TABLE\s+dbo\.(?<name>\w+)\s*\((?<body>.*?)\)\s*;",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ColumnLine = new(
        @"^\s*\[(?<name>\w+)\]\s+(?<type>\w+)(?:\s*\((?<length>[^)]*)\))?(?:\s+IDENTITY\s*\([^)]*\))?\s+(?<nullability>NOT\s+NULL|NULL)\s*,?\s*$",
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
            columns.Add(new SqlColumn(
                ClrTypeFor(sqlType),
                DecimalPrecisionFor(sqlType, column.Groups["length"].Value),
                isNullable,
                MaxLengthFor(sqlType, column.Groups["length"].Value),
                column.Groups["name"].Value,
                sqlType));
        }

        return columns;
    }

    // The bounded character length of a string column, or null for a non-string column and for a
    // length token that is absent or MAX.
    private static int? MaxLengthFor(string sqlType, string lengthToken)
    {
        if (ClrTypeFor(sqlType) != typeof(string))
        {
            return null;
        }

        return int.TryParse(lengthToken, out int length) ? length : null;
    }

    // The (precision, scale) of a DECIMAL(p,s)/NUMERIC(p,s) column, or null for MONEY (no (p,s) token)
    // and every non-decimal type. lengthToken is the raw "p,s" capture. A DECIMAL/NUMERIC without an
    // explicit (p,s) throws rather than returning null, so the parity tests cannot silently drop it.
    private static (int Precision, int Scale)? DecimalPrecisionFor(string sqlType, string lengthToken)
    {
        if (sqlType is not ("DECIMAL" or "NUMERIC"))
        {
            return null;
        }

        string[] parts = lengthToken.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int precision) && int.TryParse(parts[1], out int scale)
            ? (precision, scale)
            : throw new InvalidOperationException($"{sqlType} column without an explicit (p,s): '{lengthToken}'.");
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
