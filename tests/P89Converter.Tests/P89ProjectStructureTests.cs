using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TextToXml.Tests;

namespace P89Converter.Tests;

// The P89 format lives beside the generic library, never inside it (Story 5.1, AC-FR16-4 unchanged).
[Trait("Category", TestCategory.Unit)]
public class P89ProjectStructureTests
{
    // AC-FR22-7: P89Converter references exactly FichierJournal (the journal contract, D31) and
    // TextToXml, and TextToXml carries no P89 artifact.
    [Fact]
    [Trait("AC", "FR22-7")]
    public void P89Converter_ReferencesOnlyFichierJournalAndTextToXml_AcFr22_7()
    {
        string[] references = XDocument.Load(RepoLayout.ProjectFile("src/P89Converter/P89Converter.csproj"))
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["FichierJournal", "TextToXml"], references);

        string[] p89InLibrary = Directory
            .EnumerateFiles(RepoLayout.ProjectFile("src/TextToXml"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Replace('\\', '/').Contains("/obj/") && !path.Replace('\\', '/').Contains("/bin/"))
            .Where(path => File.ReadAllText(path).Contains("P89", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(p89InLibrary);
    }
}
