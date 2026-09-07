using System.Threading;
using System.Threading.Tasks;
using Kape22Importer.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kape22Importer;

// Runs the FR-8 startup compatibility check before the Worker starts. A StartupCompatibilityException
// from StartAsync aborts host startup, so an incompatible deployment fails loudly instead of rejecting
// every Fichier at runtime (Story 2.5). The check only reads the built EF model and the embedded
// Descripteur, so it opens no database connection.
public sealed class StartupCompatibilityHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        AscoLsiDbContext context = scope.ServiceProvider.GetRequiredService<AscoLsiDbContext>();

        StartupCompatibilityCheck.Verify(context.Model, EmbeddedDescriptor.Xml);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
