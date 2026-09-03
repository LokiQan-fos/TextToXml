using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TextToXml.Tests;

// Story 1.8 - Format isolation: adding a format costs zero lines of code in TextToXml (FR-16).
// These are architecture / compile-barrier tests: their failure form is a broken build or a structural
// assertion, so the clean red-to-green ceremony of CC-1 does not apply (see epics.md CC-1). They must
// stay green for every later story.
// Vocabulary follows the PRD glossary section 3 (CC-5).
[Trait("Category", TestCategory.Unit)]
public class FormatIsolationTests
{
    // Quoted string literals that would only make sense for the P60 / KAPE22 layout. Numeric Position
    // literals (for example the value 9) cannot be scanned without false positives and are left to code
    // review and the genericity fixtures (AC-FR1-9, AC-FR5-13).
    private static readonly string[] ForbiddenLiterals =
    [
        "\"P60\"", "\"KAPE22\"", "\"EOF\"", "\"Segment\"", "\"000\"", "\"999\"",
        "\"847\"", "\"682\"", "\"DiametreProduit\"", "\"Coulee\"", "\"Records\"",
    ];

    // AC-FR16-1: the only shared code Kape22Importer references is TextToXml and PortalSharedLibrary.
    [Fact]
    [Trait("AC", "FR16-1")]
    public void Kape22Importer_ReferencesOnlyTextToXmlAndPortalSharedLibrary_AcFr16_1()
    {
        string[] references = Includes("src/Kape22Importer/Kape22Importer.csproj", "ProjectReference")
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "PortalSharedLibrary", "TextToXml" }, references);
    }

    // AC-FR16-2: the variation points of a format (<format>.xml, <format>.xsd, the DTO, the entity and
    // its DbContext, the mapping table, appsettings) all live in the importer, never in TextToXml. So
    // the library project carries no descriptor, no schema and no embedded resource.
    [Fact]
    [Trait("AC", "FR16-2")]
    public void TextToXml_CarriesNoFormatArtifact_AcFr16_2()
    {
        string projectDirectory = Path.Combine(RepoLayout.RepoRoot, "src", "TextToXml");

        string[] formatFiles = Directory
            .EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/obj/") && !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(formatFiles.Length == 0, $"TextToXml must carry no format artifact. Found: {string.Join(", ", formatFiles)}.");

        Assert.Empty(Includes("src/TextToXml/TextToXml.csproj", "EmbeddedResource"));
    }

    // AC-FR16-3: the TextToXml test suite depends on no *Importer project.
    [Fact]
    [Trait("AC", "FR16-3")]
    public void TextToXmlTests_ReferenceNoImporterProject_AcFr16_3()
    {
        string[] references = Includes("tests/TextToXml.Tests/TextToXml.Tests.csproj", "ProjectReference")
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
            .ToArray();

        Assert.Equal(new[] { "TextToXml" }, references);
        Assert.DoesNotContain(references, name => name.EndsWith("Importer", StringComparison.OrdinalIgnoreCase));
    }

    // AC-FR16-4: no TextToXml source file carries a string literal that is specific to P60; only
    // Windows-1252 stays hard-coded (checked here to stay visible, not forbidden).
    [Fact]
    [Trait("AC", "FR16-4")]
    public void TextToXmlSource_ContainsNoP60SpecificLiteral_AcFr16_4()
    {
        string projectDirectory = Path.Combine(RepoLayout.RepoRoot, "src", "TextToXml");

        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string literal in ForbiddenLiterals.Where(text.Contains))
            {
                offenders.Add($"{Path.GetFileName(file)} contains {literal}");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));

        // Windows-1252 is the one encoding the PRD lets the library hard-code (AR-10, NFR-4); some
        // source file must still pin it, wherever the decoder setup lives.
        bool pinsWindows1252 = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Replace('\\', '/').Contains("/obj/") && !file.Replace('\\', '/').Contains("/bin/"))
            .Any(file => File.ReadAllText(file).Contains("1252"));
        Assert.True(pinsWindows1252, "No src/TextToXml source pins Windows-1252 (expected in the input decoder).");
    }

    private static List<string> Includes(string projectRelativePath, string elementName)
    {
        XDocument document = XDocument.Load(RepoLayout.ProjectFile(projectRelativePath));

        return document.Descendants()
            .Where(e => e.Name.LocalName == elementName)
            .Select(e => (string?)e.Attribute("Include"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();
    }
}
