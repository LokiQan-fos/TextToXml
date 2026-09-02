using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TextToXml.Tests;

// Guards the structural contract of the solution: the TextToXml purity rule (NFR-4, CC-6, AC-FR16-4),
// the shared framework settings the Story 1.1 acceptance criteria mandate, and the reference boundary
// of Kape22Importer (AR-1, AR-2, AC-FR16-1). These stay green for every later story.
[Trait("Category", TestCategory.Unit)]
public class SolutionStructureTests
{
    // The single NuGet package the library is permitted to carry, per NFR-4 and the Story 1.1 criteria.
    // It is currently supplied implicitly by the net10.0 SDK, so the csproj declares nothing.
    private static readonly HashSet<string> AllowedRuntimePackages =
        new(StringComparer.OrdinalIgnoreCase) { "System.Text.Encoding.CodePages" };

    // The only PackageReference Kape22Importer may declare beyond the shared framework (AR-1, AC-FR16-1).
    private static readonly HashSet<string> AllowedImporterPackages =
        new(StringComparer.OrdinalIgnoreCase) { "Microsoft.Extensions.Hosting" };

    [Fact]
    public void TextToXml_UsesTheClassLibrarySdk()
    {
        Assert.Equal("Microsoft.NET.Sdk", SdkOf("src/TextToXml/TextToXml.csproj"));
    }

    [Fact]
    public void Kape22Importer_UsesTheWorkerSdk()
    {
        Assert.Equal("Microsoft.NET.Sdk.Worker", SdkOf("src/Kape22Importer/Kape22Importer.csproj"));
    }

    [Fact]
    public void DirectoryBuildProps_PinsTheFrameworkContract()
    {
        Dictionary<string, string> properties = MsBuildProperties("Directory.Build.props");

        Assert.Equal("net10.0", properties.GetValueOrDefault("TargetFramework"));
        Assert.Equal("enable", properties.GetValueOrDefault("Nullable"));
        Assert.Equal("latest", properties.GetValueOrDefault("LangVersion"));
    }

    [Fact]
    public void TextToXml_DeclaresNoDisallowedPackageReference()
    {
        string[] forbidden = PackageReferencesOf("src/TextToXml/TextToXml.csproj")
            .Where(p => !AllowedRuntimePackages.Contains(p))
            .ToArray();

        Assert.True(forbidden.Length == 0, $"TextToXml must stay BCL-only. Unexpected package(s): {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void TextToXml_ResolvesNoDisallowedRuntimePackage()
    {
        // Reads the restore output so a package pulled in through Directory.Build.props, central package
        // management, or a transitive dependency is caught, not only a literal PackageReference in the csproj.
        string[] resolved = ResolvedPackagesOf("src/TextToXml/obj/project.assets.json")
            .Where(p => !AllowedRuntimePackages.Contains(p))
            .ToArray();

        Assert.True(resolved.Length == 0, $"TextToXml resolved a non-BCL package: {string.Join(", ", resolved)}.");
    }

    [Fact]
    public void SharedBuildFiles_AddNoPackageReference()
    {
        // A PackageReference in Directory.Build.props would silently flow into TextToXml.
        foreach (string sharedFile in new[] { "Directory.Build.props", "Directory.Packages.props" })
        {
            Assert.Empty(ElementValues(sharedFile, "PackageReference"));
        }
    }

    [Fact]
    public void TextToXml_HasNoProjectReference()
    {
        Assert.Empty(ElementValues("src/TextToXml/TextToXml.csproj", "ProjectReference"));
    }

    [Fact]
    public void Kape22Importer_DeclaresOnlyAllowedPackageReferences()
    {
        string[] forbidden = PackageReferencesOf("src/Kape22Importer/Kape22Importer.csproj")
            .Where(p => !AllowedImporterPackages.Contains(p))
            .ToArray();

        Assert.True(forbidden.Length == 0, $"Kape22Importer declared an unexpected package: {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void Kape22Importer_ReferencesOnlyTextToXmlAndPortalSharedLibrary()
    {
        string[] references = ElementValues("src/Kape22Importer/Kape22Importer.csproj", "ProjectReference")
            .Select(ProjectName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            references.SequenceEqual(new[] { "PortalSharedLibrary", "TextToXml" }),
            $"Kape22Importer must reference exactly TextToXml and PortalSharedLibrary. Found: {string.Join(", ", references)}.");
    }

    private static string ProjectName(string includePath) =>
        Path.GetFileNameWithoutExtension(includePath.Replace('\\', '/'));

    private static List<string> PackageReferencesOf(string projectRelativePath) =>
        ElementValues(projectRelativePath, "PackageReference");

    private static string SdkOf(string projectRelativePath) =>
        (string?)LoadProject(projectRelativePath).Root?.Attribute("Sdk") ?? string.Empty;

    private static Dictionary<string, string> MsBuildProperties(string projectRelativePath)
    {
        XDocument document = LoadProject(projectRelativePath);

        return document.Descendants()
            .Where(e => e.Parent is not null && e.Parent.Name.LocalName == "PropertyGroup")
            .GroupBy(e => e.Name.LocalName)
            .ToDictionary(g => g.Key, g => g.Last().Value.Trim());
    }

    private static List<string> ElementValues(string projectRelativePath, string elementName)
    {
        XDocument document = LoadProject(projectRelativePath);

        return document.Descendants()
            .Where(e => e.Name.LocalName == elementName)
            .Select(e => (string?)e.Attribute("Include"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();
    }

    private static XDocument LoadProject(string projectRelativePath)
    {
        string path = RepoLayout.ProjectFile(projectRelativePath);
        Assert.True(File.Exists(path), $"Expected project file not found: {projectRelativePath}.");

        try
        {
            return XDocument.Load(path);
        }
        catch (XmlException exception)
        {
            throw new Xunit.Sdk.XunitException($"{projectRelativePath} is not well-formed XML: {exception.Message}");
        }
    }

    private static IEnumerable<string> ResolvedPackagesOf(string assetsRelativePath)
    {
        string path = RepoLayout.ProjectFile(assetsRelativePath);
        Assert.True(File.Exists(path), $"Restore output not found ({assetsRelativePath}). Run 'dotnet restore' first.");

        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(path));
        if (!assets.RootElement.TryGetProperty("libraries", out JsonElement libraries))
        {
            yield break;
        }

        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            bool isPackage = library.Value.TryGetProperty("type", out JsonElement type)
                && type.GetString() == "package";
            if (isPackage)
            {
                yield return library.Name.Split('/')[0];
            }
        }
    }
}
