namespace TextToXml.Tests;

// Trait values for splitting the suite. "dotnet test --filter Category=Unit" must run without a database.
// "Category=Integration" needs a reachable local SQL Server test instance (AR-12); those tests skip with a
// clear message when none is configured.
public static class TestCategory
{
    public const string Integration = "Integration";

    public const string Unit = "Unit";
}
