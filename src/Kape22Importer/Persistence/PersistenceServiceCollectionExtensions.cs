using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kape22Importer.Persistence;

// Wires AscoLsiDbContext from configuration. The connection string is read from
// ConnectionStrings:AscoLSI and never hard-coded (NFR-5, CC-7); a missing value fails fast at startup
// rather than at the first query.
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAscoLsiPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("AscoLSI")
            ?? throw new InvalidOperationException(
                "Connection string 'AscoLSI' is not configured. Set ConnectionStrings:AscoLSI.");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'AscoLSI' is empty. Set ConnectionStrings:AscoLSI.");
        }

        services.AddDbContext<AscoLsiDbContext>(options => options.UseSqlServer(connectionString));

        return services;
    }
}
