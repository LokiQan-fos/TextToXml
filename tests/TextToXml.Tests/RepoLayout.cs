using System;
using System.IO;

namespace TextToXml.Tests;

// Locates repository paths from the test output directory so structural tests do not hard-code absolute paths.
public static class RepoLayout
{
    // Resolved lazily so a missing solution file fails one test with a clear message instead of a type initializer.
    private static readonly Lazy<string> LazyRoot = new(FindRoot);

    public static string FixturesDirectory => Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static string RepoRoot => LazyRoot.Value;

    public static string ProjectFile(string relativePath) =>
        Path.Combine(LazyRoot.Value, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TextToXml.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new DirectoryNotFoundException("Could not locate TextToXml.sln above the test output directory.");
        }

        return dir.FullName;
    }
}
