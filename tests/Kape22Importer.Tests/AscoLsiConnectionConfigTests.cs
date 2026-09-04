using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.1 + NFR-5 / CC-7: the AscoLSI connection string is read from IConfiguration, never
// hard-coded. The MQTTnetServices string is wired in Epic 3 when its entities land. No database is
// touched, so these stay in the Unit category.
[Trait("Category", TestCategory.Unit)]
[Trait("AC", "2.1")]
public class AscoLsiConnectionConfigTests
{
    [Fact]
    public void AddAscoLsiPersistence_ReadsTheConnectionStringFromConfiguration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AscoLSI"] = "Server=from-config;Database=AscoLSI_Test;Trusted_Connection=True;",
            })
            .Build();

        ServiceCollection services = new();
        services.AddAscoLsiPersistence(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        using AscoLsiDbContext context = provider.GetRequiredService<AscoLsiDbContext>();

        Assert.Contains("from-config", context.Database.GetConnectionString());
    }

    [Fact]
    public void AddAscoLsiPersistence_ThrowsWhenTheConnectionStringIsMissing()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAscoLsiPersistence(empty));
    }

    [Fact]
    public void ImporterSource_ContainsNoHardCodedConnectionString()
    {
        // A literal "Server=" / "Data Source=" in production code would bypass IConfiguration (CC-7).
        Regex connectionLiteral = new(@"(Server|Data Source)\s*=", RegexOptions.IgnoreCase);

        string sourceRoot = RepoLayout.ProjectFile("src/Kape22Importer");
        string[] offenders = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => connectionLiteral.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Hard-coded connection string in: {string.Join(", ", offenders)}.");
    }
}
