using Xunit;

namespace Kape22Importer.Tests;

// Every database integration class joins this collection so the AR-12 harness (probe + schema apply)
// runs once for the assembly, not once per class.
[CollectionDefinition(Name)]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>
{
    public const string Name = "SqlServerIntegration";
}
