namespace TextToXml.Tests;

// Trait values for splitting the suite. "dotnet test --filter Category=Unit" must run without Docker.
// "Category=Integration" needs a Docker daemon for the SQL Server container (AR-12).
public static class TestCategory
{
    public const string Integration = "Integration";

    public const string Unit = "Unit";
}
