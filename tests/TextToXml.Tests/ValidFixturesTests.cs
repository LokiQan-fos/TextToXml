using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TextToXml.Tests;

// The 10 reference P60 files (Annexe A.4) are the backbone of the whole suite. This checks they ship next to the tests.
[Trait("Category", TestCategory.Unit)]
public class ValidFixturesTests
{
    private static readonly Regex ReferenceFileName = new(@"^P60_847_682_\d{3}$", RegexOptions.Compiled);

    private static readonly string[] ExpectedNames =
        Enumerable.Range(1, 10).Select(n => $"P60_847_682_{n:D3}").ToArray();

    public static TheoryData<string> ValidFixtureNames()
    {
        TheoryData<string> data = new();
        foreach (string name in ExpectedNames)
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void FixtureTree_HasValidAndGenericFolders()
    {
        Assert.True(Directory.Exists(Path.Combine(RepoLayout.FixturesDirectory, "valid")));
        Assert.True(Directory.Exists(Path.Combine(RepoLayout.FixturesDirectory, "generic")));
    }

    [Fact]
    public void ValidFolder_HoldsTheTenReferenceFiles()
    {
        string validDirectory = Path.Combine(RepoLayout.FixturesDirectory, "valid");
        Assert.True(Directory.Exists(validDirectory), $"Missing fixtures directory: {validDirectory}.");

        // Match only the reference file name pattern so stray OS metadata (Thumbs.db, .gitkeep) is ignored.
        string[] referenceFiles = Directory.GetFiles(validDirectory)
            .Select(Path.GetFileName)
            .Where(name => name is not null && ReferenceFileName.IsMatch(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(ExpectedNames.OrderBy(name => name, StringComparer.Ordinal), referenceFiles);
    }

    [Theory]
    [MemberData(nameof(ValidFixtureNames))]
    public void ValidFixture_IsPresentAndMatchesTheRepositorySample(string fileName)
    {
        string copied = Path.Combine(RepoLayout.FixturesDirectory, "valid", fileName);
        string sample = Path.Combine(RepoLayout.RepoRoot, "P60", fileName);

        Assert.True(File.Exists(sample), $"Repository sample not found: {sample}.");
        Assert.True(File.Exists(copied), $"Missing fixture {fileName} in the test output.");
        Assert.Equal(File.ReadAllBytes(sample), File.ReadAllBytes(copied));
    }
}
