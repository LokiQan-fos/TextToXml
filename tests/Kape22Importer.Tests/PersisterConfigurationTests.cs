using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kape22Importer.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.8 / AC-FR11-8 (D21, CC-7): the persister never hard-codes a connection string - the AscoLSI
// connection reaches the DbContext through IConfiguration. AddAscoLsiPersistence is already covered by
// AscoLsiConnectionConfigTests; this keeps a guard attached to the AC-FR11-8 name and re-runs the
// literal scan now that Kape22Persister.cs exists. The "connection flows from configuration" behaviour
// is exercised end to end by TransactionalPersistenceTests. Written test-first (CC-1). Unit-only (AR-12).
[Trait("Category", TestCategory.Unit)]
public class PersisterConfigurationTests
{
    // AC-FR11-8: no source file under src/Kape22Importer carries a literal connection string.
    [Fact]
    [Trait("AC", "FR11-8")]
    public void ImporterSource_HasNoHardCodedConnectionString_AcFr11_8()
    {
        Regex connectionLiteral = new(@"(Server|Data Source)\s*=", RegexOptions.IgnoreCase);

        string sourceRoot = RepoLayout.ProjectFile("src/Kape22Importer");
        IEnumerable<string> productionSources = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        string[] offenders = productionSources
            .Where(path => connectionLiteral.IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToArray()!;

        Assert.True(offenders.Length == 0, $"Hard-coded connection string in: {string.Join(", ", offenders)}.");
    }

    // AC-FR11-8: an Import:InitiatingServer longer than the L_D_LOG_COMMANDE.User column is a
    // configuration error, caught at construction with a clear message rather than turning every import
    // into a PersistenceError.
    [Fact]
    [Trait("AC", "FR11-8")]
    public void Constructor_RejectsInitiatingServerLongerThanUserColumn_AcFr11_8()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:InitiatingServer"] = new string('X', LogCommandeColumnLengths.User + 1),
            })
            .Build();

        // The persister constructor validates configuration only; it never touches the context, so a
        // provider-less options object is enough here.
        using AscoLsiDbContext context = new(new DbContextOptionsBuilder<AscoLsiDbContext>().Options);

        Assert.Throws<ArgumentException>(() => new Kape22Persister(context, configuration));
    }
}
