using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 3.1 / AC-FR12-8 (CC-7): every FR-12 path, the polling interval, Import:InitiatingServer and
// Import:RetentionDays come from IConfiguration - no value is hard-coded in InboxScanner or its
// options. Written test-first (CC-1).
[Trait("Category", TestCategory.Unit)]
public class ImportOptionsTests
{
    // AC-FR12-8: the whole FR-12 configuration binds from the "Import" section.
    [Fact]
    [Trait("AC", "FR12-8")]
    public void ImportOptions_BindEveryValueFromConfiguration_AcFr12_8()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:ArchiveFolder"] = "archive",
                ["Import:ErrorFolder"] = "error",
                ["Import:InboxPath"] = @"D:\Site-FTP\Reception\GPAO",
                ["Import:InitiatingServer"] = "AFS017",
                ["Import:PollingInterval"] = "00:00:30",
                ["Import:ProcessingFolder"] = "processing",
                ["Import:RetentionDays"] = "30",
            })
            .Build();

        ImportOptions options = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;

        Assert.Equal("archive", options.ArchiveFolder);
        Assert.Equal("error", options.ErrorFolder);
        Assert.Equal(@"D:\Site-FTP\Reception\GPAO", options.InboxPath);
        Assert.Equal("AFS017", options.InitiatingServer);
        Assert.Equal(TimeSpan.FromSeconds(30), options.PollingInterval);
        Assert.Equal("processing", options.ProcessingFolder);
        Assert.Equal(30, options.RetentionDays);
    }

    // AC-FR12-8: the values shipped in src/Kape22Importer/appsettings.json bind to a well-typed,
    // non-default ImportOptions, so a renamed key or an unparseable interval fails here rather than
    // at runtime once the worker (Story 3.4) wires the bind.
    [Fact]
    [Trait("AC", "FR12-8")]
    public void ImportOptions_BindEveryValueFromShippedAppsettings_AcFr12_8()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoLayout.ProjectFile("src/Kape22Importer/appsettings.json"), optional: false)
            .Build();

        ImportOptions options = configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>()!;

        Assert.NotEmpty(options.ArchiveFolder);
        Assert.NotEmpty(options.ErrorFolder);
        Assert.NotEmpty(options.InboxPath);
        Assert.NotEmpty(options.ProcessingFolder);
        Assert.True(options.PollingInterval > TimeSpan.Zero);
        Assert.True(options.RetentionDays > 0);
    }

    // AC-FR12-8: no source file under src/Kape22Importer carries an absolute filesystem path literal
    // (the reception root, the working sub-folders and the polling cadence all come from config).
    [Fact]
    [Trait("AC", "FR12-8")]
    public void ImporterSource_HasNoHardCodedReceptionPath_AcFr12_8()
    {
        Regex absolutePathLiteral = new(@"""[A-Za-z]:\\|Site-FTP", RegexOptions.IgnoreCase);

        string sourceRoot = RepoLayout.ProjectFile("src/Kape22Importer");
        string[] offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => absolutePathLiteral.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToArray()!;

        Assert.True(offenders.Length == 0, $"Hard-coded reception path in: {string.Join(", ", offenders)}.");
    }
}
