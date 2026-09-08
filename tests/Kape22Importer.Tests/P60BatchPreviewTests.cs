using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using TextToXml;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Batch dry-run over every P60 Fichier dropped in the repo's P60/ folder: runs Step 1
// (Converter -> normalized XML) and Step 2's in-memory mapping (Kape22Mapper.Map), writes the
// generated XML to artifacts/xml-preview/ for inspection, and fails if any Fichier produces an
// error. No database: this is the "see the XML before it hits AscoLSI" harness.
//
//   dotnet test --filter "FullyQualifiedName~P60BatchPreviewTests"
//
// then open artifacts/xml-preview/<name>.xml (artifacts/ is gitignored).
[Trait("Category", TestCategory.Unit)]
public class P60BatchPreviewTests
{
    private static readonly Regex FichierName = new(@"^P60_\d+_\d+_\d+$", RegexOptions.Compiled);

    private static string P60Directory => RepoLayout.ProjectFile("P60");

    private static string PreviewDirectory => RepoLayout.ProjectFile("artifacts/xml-preview");

    public static TheoryData<string> P60Fichiers()
    {
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(P60Directory)
                     .Where(p => FichierName.IsMatch(Path.GetFileName(p)))
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(P60Fichiers))]
    public void P60Fichier_ConvertsAndMapsWithoutError(string fichierName)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(P60Directory, fichierName));

        ConversionResult conversion = Converter.Convert(bytes, EmbeddedDescriptor.Xml);

        if (conversion.Xml is not null)
        {
            Directory.CreateDirectory(PreviewDirectory);
            File.WriteAllText(Path.Combine(PreviewDirectory, fichierName + ".xml"), conversion.Xml, new UTF8Encoding(false));
        }

        Assert.True(conversion.Success, $"Step 1 failed for {fichierName}:\n{Describe(conversion.Errors)}");

        MapResult<L_D_KAPE22> mapping = new Kape22Mapper().Map(conversion.Xml!, fichierName);

        Assert.True(mapping.Success, $"Step 2 mapping failed for {fichierName}:\n{Describe(mapping.Errors)}");
    }

    private static string Describe(System.Collections.Generic.IReadOnlyList<ConversionError> errors) =>
        string.Join("\n", errors.Select(e => $"  [{e.Code}] {e.Block} {e.FieldId} L{e.LineNumber}: {e.Message}"));
}
