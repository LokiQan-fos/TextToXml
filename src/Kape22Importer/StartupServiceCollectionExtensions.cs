using Kape22Importer.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kape22Importer;

// The importer's composition root, kept in one place so a test can compose the exact same graph the
// real host does (Story 2.5). The registration order matters: the FR-8 compatibility check is a
// hosted service placed ahead of the Worker, so an incompatible Descripteur/table pair aborts host
// startup instead of failing per Fichier.
public static class StartupServiceCollectionExtensions
{
    public static IServiceCollection AddKape22Startup(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAscoLsiPersistence(configuration);
        services.AddHostedService<StartupCompatibilityHostedService>();
        services.AddHostedService<Worker>();

        return services;
    }
}
