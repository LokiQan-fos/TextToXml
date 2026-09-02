using TextToXml.Tests;

namespace Kape22Importer.Tests;

// Placeholder that anchors the Integration category so "dotnet test --filter Category=Integration" resolves to a
// real test from day one. The Docker-backed SQL Server harness itself lands in Story 2.1 (AR-12).
[Trait("Category", TestCategory.Integration)]
public class IntegrationHarnessTests
{
    [Fact(Skip = "Docker-backed SQL Server harness is built in Story 2.1.")]
    public void DockerBackedDatabaseHarness_IsBuiltInStory21()
    {
    }
}
