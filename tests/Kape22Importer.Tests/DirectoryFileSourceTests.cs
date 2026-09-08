using System;
using System.IO;
using System.Linq;
using System.Text;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 3.1: DirectoryFileSource is the production IFileSource - a thin adapter over System.IO rooted
// at Import:InboxPath. The FR-12 behaviour is covered by InboxScannerTests over the in-memory fake;
// this checks the adapter itself round-trips against a real temp directory (no database, no Docker).
// Written test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public sealed class DirectoryFileSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "kape22-filesource-" + Guid.NewGuid().ToString("N"));

    private DirectoryFileSource Source => new(this.root);

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    [Fact]
    public void Write_ThenReadAndList_RoundTripsTheContent()
    {
        byte[] content = Encoding.UTF8.GetBytes("payload");

        this.Source.Write("", "P60_847_682_001", content);

        Assert.Equal(content, this.Source.Read("", "P60_847_682_001"));
        FichierEntry entry = Assert.Single(this.Source.List(""));
        Assert.Equal("P60_847_682_001", entry.Name);
        Assert.Equal(content.Length, entry.Length);
    }

    [Fact]
    public void List_OnMissingFolder_IsEmpty() => Assert.Empty(this.Source.List("processing"));

    [Fact]
    public void List_ReturnsOnlyDirectChildren_NotNestedFiles()
    {
        this.Source.Write("archive", "top.xml", [1]);
        this.Source.Write("archive/2026/09", "nested.xml", [2]);

        Assert.Equal(["top.xml"], this.Source.List("archive").Select(entry => entry.Name));
    }

    [Fact]
    public void Move_RelocatesTheFile_CreatingTheTargetFolderTree()
    {
        this.Source.Write("processing", "P60_847_682_001", Encoding.UTF8.GetBytes("done"));

        this.Source.Move("processing", "P60_847_682_001", "archive/2026/09", "P60_847_682_001");

        Assert.Empty(this.Source.List("processing"));
        Assert.Equal("done", Encoding.UTF8.GetString(this.Source.Read("archive/2026/09", "P60_847_682_001")));
    }

    [Fact]
    public void ListRecursive_FindsNestedFiles_WithTheirRelativeFolder()
    {
        this.Source.Write("archive/2026/09", "a.xml", [1]);
        this.Source.Write("archive/2026/08", "b.xml", [2]);

        FichierEntry nested = Assert.Single(this.Source.ListRecursive("archive"), entry => entry.Name == "a.xml");
        Assert.Equal("archive/2026/09", nested.Folder);
        Assert.Equal(2, this.Source.ListRecursive("archive").Count);
    }

    [Fact]
    public void Delete_RemovesTheFile_AndIsSilentWhenItIsMissing()
    {
        this.Source.Write("error", "gone.errors.json", [1]);

        this.Source.Delete("error", "gone.errors.json");
        this.Source.Delete("error", "never-existed.json");

        Assert.Empty(this.Source.List("error"));
    }

    [Fact]
    public void Delete_RemovesAFileFromANestedFolder()
    {
        this.Source.Write("archive/2026/09", "P60_847_682_001.xml", [1]);

        this.Source.Delete("archive/2026/09", "P60_847_682_001.xml");

        Assert.Empty(this.Source.List("archive/2026/09"));
    }
}
