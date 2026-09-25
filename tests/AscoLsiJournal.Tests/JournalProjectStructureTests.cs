using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TextToXml.Tests;

namespace AscoLsiJournal.Tests;

// The journal boundary (Story 5.0, D31): the interface depends on nothing, the LSI implementation only
// on the interface and EF Core, so a format can journal without knowing LSI.
[Trait("Category", TestCategory.Unit)]
public class JournalProjectStructureTests
{
    // AC-FR23-1: FichierJournal carries no reference; AscoLsiJournal references FichierJournal and
    // Microsoft.EntityFrameworkCore.SqlServer only.
    [Fact]
    [Trait("AC", "FR23-1")]
    public void JournalProjects_KeepTheirReferenceBoundaries_AcFr23_1()
    {
        Assert.Empty(Includes("src/FichierJournal/FichierJournal.csproj", "ProjectReference"));
        Assert.Empty(Includes("src/FichierJournal/FichierJournal.csproj", "PackageReference"));

        Assert.Equal(["FichierJournal"], Includes("src/AscoLsiJournal/AscoLsiJournal.csproj", "ProjectReference"));
        Assert.Equal(
            ["Microsoft.EntityFrameworkCore.SqlServer"],
            Includes("src/AscoLsiJournal/AscoLsiJournal.csproj", "PackageReference"));
    }

    // A project reference is reduced to its project name; a package id is kept whole (it contains dots).
    private static string[] Includes(string project, string element) =>
        [.. XDocument.Load(RepoLayout.ProjectFile(project))
            .Descendants(element)
            .Select(reference => (string)reference.Attribute("Include")!)
            .Select(include => element == "ProjectReference"
                ? Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))
                : include)
            .Order(StringComparer.Ordinal)];
}
