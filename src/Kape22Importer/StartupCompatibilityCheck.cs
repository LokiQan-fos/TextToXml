using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Kape22Importer;

// Story 2.5 (FR-8): before the worker processes any Fichier, the embedded Descripteur must describe
// only things L_D_KAPE22 can accept. An incompatibility here is a deployment fault, raised once at
// startup, never a per-file rejection. Verify inspects the built EF model and the embedded P60.xml and
// throws a single StartupCompatibilityException listing every offending pair (AC-FR8-1, AC-FR8-2,
// AC-FR8-3); a compatible pair returns without throwing (AC-FR8-4).
public static class StartupCompatibilityCheck
{
    // NOT NULL columns of L_D_KAPE22 that have no Detail Champ and are filled by an FR-9 derived rule
    // instead (PRD Annexe B): NumeroFichier from the Header roulette, DateReception from the worker
    // clock. AC-FR8-3 treats these as sourced.
    public static readonly IReadOnlySet<string> DerivedRequiredColumns =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "DateReception",
            "NumeroFichier",
        };

    // The datatype the Descripteur must carry for a Champ mapped onto a column of the given CLR type.
    private static readonly IReadOnlyDictionary<Type, string> DatatypeForClrType =
        new Dictionary<Type, string>
        {
            [typeof(int)] = "int",
            [typeof(decimal)] = "decimal",
            [typeof(DateTime)] = "datetime",
            [typeof(string)] = "string",
        };

    public static void Verify(IModel model, string descriptorXml)
    {
        IEntityType entity = model.FindEntityType(typeof(L_D_KAPE22))
            ?? throw new InvalidOperationException("The EF model has no L_D_KAPE22 entity type.");

        XElement message = ParseMessage(descriptorXml);

        List<string> problems = [];
        HashSet<string> mappedColumns = new(StringComparer.Ordinal);

        foreach (XElement champ in message.Elements("value"))
        {
            string id = (string?)champ.Attribute("Id")
                ?? throw new StartupCompatibilityException(
                    "The embedded P60 Descripteur has a <value> element with no Id attribute.");
            if (Kape22Mapper.IsIgnored(id))
            {
                continue;
            }

            string columnName = Kape22Mapper.ResolveTargetName(id);
            IProperty? column = entity.GetProperties()
                .FirstOrDefault(property => string.Equals(property.Name, columnName, StringComparison.OrdinalIgnoreCase));
            if (column is null)
            {
                continue;
            }

            mappedColumns.Add(column.Name);
            string datatype = (string?)champ.Attribute("datatype") ?? "string";
            Type clrType = Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;

            // AC-FR8-1: the Champ datatype must fit the column CLR type.
            if (!DatatypeForClrType.TryGetValue(clrType, out string? expectedDatatype) || datatype != expectedDatatype)
            {
                problems.Add(
                    $"Champ '{id}' datatype='{datatype}' is incompatible with column {column.Name} ({clrType.Name}).");
                continue;
            }

            // AC-FR8-2: a mapped string Champ must fit within the column max_length. Size is only read
            // here, inside the string branch, because a non-string Champ carries no length to check.
            if (datatype == "string")
            {
                int? maxLength = column.GetMaxLength();
                if (!int.TryParse(
                    (string?)champ.Attribute("Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
                {
                    problems.Add($"Champ '{id}' has no valid Size attribute.");
                }
                else if (maxLength is not null && size > maxLength)
                {
                    problems.Add(
                        $"Champ '{id}' Size={size} exceeds max_length={maxLength} of column {column.Name}.");
                }
            }
        }

        // AC-FR8-3: every NOT NULL column must have a source, a mapped Champ or an FR-9 derived rule.
        foreach (IProperty column in entity.GetProperties())
        {
            if (column.IsNullable || column.Name == "Id")
            {
                continue;
            }

            if (!mappedColumns.Contains(column.Name) && !DerivedRequiredColumns.Contains(column.Name))
            {
                problems.Add(
                    $"NOT NULL column '{column.Name}' has no source (no mapped Champ and no derived rule).");
            }
        }

        if (problems.Count > 0)
        {
            throw new StartupCompatibilityException(
                "The embedded P60 Descripteur is not compatible with L_D_KAPE22:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }
    }

    // Parses the descriptor XML and returns its <message> section. A malformed or structurally wrong
    // Descripteur is a deployment fault, so it surfaces as the same StartupCompatibilityException as an
    // incompatible pair rather than a raw XmlException.
    private static XElement ParseMessage(string descriptorXml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(descriptorXml);
        }
        catch (XmlException error)
        {
            throw new StartupCompatibilityException(
                "The embedded P60 Descripteur is not well-formed XML.", error);
        }

        return document.Root?.Element("message")
            ?? throw new StartupCompatibilityException(
                "The embedded P60 Descripteur has no <message> section.");
    }
}

// Thrown at worker startup when the Descripteur and L_D_KAPE22 are not compatible. The Message lists
// every offending Champ/column pair so the deployment can be fixed in one pass.
public sealed class StartupCompatibilityException : Exception
{
    public StartupCompatibilityException(string message)
        : base(message)
    {
    }

    public StartupCompatibilityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
