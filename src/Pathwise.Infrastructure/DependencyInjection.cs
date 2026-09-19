using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pathwise.Application.Ingestion;
using Pathwise.Application.Reconstruction;
using Pathwise.Infrastructure.Persistence;
using Pathwise.Infrastructure.Reconstruction;
using Pathwise.Infrastructure.Riot;

namespace Pathwise.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPathwiseInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        var connectionString = configuration.GetConnectionString("Pathwise") ?? "Data Source=data/pathwise.db";
        if (connectionString.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            var path = connectionString["Data Source=".Length..];
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(contentRoot, path));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                connectionString = $"Data Source={path}";
            }
        }
        services.AddPooledDbContextFactory<PathwiseDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<IMatchStore, EfMatchStore>();
        services.AddSingleton<RiotReconstructionMapper>();
        services.AddSingleton<IStoredReconstructionSource, EfStoredReconstructionSource>();
        services.AddHttpClient("Riot");
        services.AddSingleton<IRiotSource>(provider => new RiotApiClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("Riot"),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RiotApiClient>>()));
        return services;
    }
}
