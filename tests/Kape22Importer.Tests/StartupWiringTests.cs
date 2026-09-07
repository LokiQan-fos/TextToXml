using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TextToXml.Tests;
using Xunit;

namespace Kape22Importer.Tests;

// Story 2.5 (FR-8): the compatibility check only aborts a bad deployment if the composition root
// actually registers it as a hosted service ahead of the Worker. These tests compose the real graph
// through AddKape22Startup so a reordering or a dropped registration fails here. Written test-first
// (CC-1), Unit-only: building the graph opens no database connection.
[Trait("Category", TestCategory.Unit)]
public class StartupWiringTests
{
    // AC-FR8-4: AddKape22Startup registers the FR-8 compatibility check as a hosted service, and it
    // comes before the Worker so an incompatible deployment aborts host startup first.
    [Fact]
    [Trait("AC", "FR8-4")]
    public void AddKape22Startup_RegistersCompatibilityCheckBeforeWorker_AcFr8_4()
    {
        ServiceCollection services = new();
        services.AddKape22Startup(ModelOnlyConfiguration());

        Type?[] hostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        int checkIndex = Array.IndexOf(hostedServices, typeof(StartupCompatibilityHostedService));
        int workerIndex = Array.IndexOf(hostedServices, typeof(Worker));

        Assert.True(checkIndex >= 0, "StartupCompatibilityHostedService is not registered as a hosted service.");
        Assert.True(workerIndex >= 0, "Worker is not registered as a hosted service.");
        Assert.True(checkIndex < workerIndex, "The compatibility check must be registered before the Worker.");
    }

    // AC-FR8-4: the compatibility check resolved from the real graph completes for the nominal model
    // and Descripteur, so host startup proceeds to the Worker.
    [Fact]
    [Trait("AC", "FR8-4")]
    public void StartupHostedService_ResolvedFromRealWiring_StartsWithoutThrowing_AcFr8_4()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddKape22Startup(ModelOnlyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();
        IHostedService compatibilityCheck = provider.GetServices<IHostedService>()
            .Single(service => service is StartupCompatibilityHostedService);

        Assert.Null(Record.Exception(
            () => compatibilityCheck.StartAsync(CancellationToken.None).GetAwaiter().GetResult()));
    }

    // A configuration whose connection string is syntactically valid but never dialed: the startup
    // check reads only the built model and the embedded Descripteur.
    private static IConfiguration ModelOnlyConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AscoLSI"] = "Server=model-only;Database=AscoLSI_Test;Trusted_Connection=True;",
            })
            .Build();
}
