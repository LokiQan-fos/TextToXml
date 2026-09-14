using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Wires scripts/e2e-worker-import.ps1 into `dotnet test` so the manual "drop a Fichier, start the real
// Launcher, check AscoLSI_Test, compare to production" procedure has a pass/fail signal instead of
// living only as something someone remembers to run by hand. The script stays the single source of
// truth for the procedure (and is still runnable stand-alone for a manual replay); this test only
// invokes it and asserts its exit code.
//
// Needs a MicroServices.sln checkout next to this repo (see README's Prérequis) and pwsh on PATH -
// skips with an actionable reason otherwise, the same way the AR-12 tests skip without a SQL Server
// instance. Never runs unattended in CI (Category=Integration only): it builds and starts the real
// Launcher and its MQTT broker as OS processes bound to real ports (5050, 1883), and briefly edits the
// SVN-tracked GpaoImportP60.json in the sibling checkout (restored by the script itself, even on
// failure).
[Collection(SqlServerIntegrationCollection.Name)]
[Trait("Category", TestCategory.Integration)]
public class GpaoImportP60WorkerEndToEndTests(SqlServerIntegrationFixture fixture)
{
    private static string ScriptPath => RepoLayout.ProjectFile("scripts/e2e-worker-import.ps1");

    // Overridable so a different machine's MicroServices checkout doesn't require editing this file.
    private static string MicroServicesRoot =>
        Environment.GetEnvironmentVariable("KAPE22_TEST_MicroServicesRoot")
        ?? @"C:\Users\Administrateur\Documents\MicroServices";

    [SkippableFact]
    public void WorkerEndToEnd_ImportsArchivesAndMatchesProduction()
    {
        Skip.IfNot(fixture.Available, fixture.SkipReason ?? "SQL Server test instance unavailable.");
        Skip.IfNot(
            Directory.Exists(MicroServicesRoot),
            $"MicroServices.sln checkout not found at '{MicroServicesRoot}'. Set the " +
            "KAPE22_TEST_MicroServicesRoot environment variable or clone it there to run this test.");

        ProcessStartInfo startInfo = new("pwsh")
        {
            ArgumentList = { "-File", ScriptPath, "-MicroServicesRoot", MicroServicesRoot },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Tells the script's own production-parity step that Kape22Importer.Tests is already built and
        // loaded by this outer dotnet test run, so it can pass --no-build instead of racing that lock.
        startInfo.Environment["E2E_BUILD_ALREADY_DONE"] = "1";

        using Process process = Process.Start(startInfo)!;

        // Both streams are redirected, so they must be drained concurrently: reading stdout to
        // completion first (as a plain sequential ReadToEnd/ReadToEnd would) blocks forever if the
        // child fills the stderr pipe before exiting, since nothing is reading it yet and the child
        // then blocks on that write - a documented deadlock risk of the Process API.
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task.WaitAll(outputTask, errorTask);
        string output = outputTask.Result;
        string error = errorTask.Result;
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"scripts/e2e-worker-import.ps1 exited {process.ExitCode}:\n--- stdout ---\n{output}\n--- stderr ---\n{error}");
    }
}
