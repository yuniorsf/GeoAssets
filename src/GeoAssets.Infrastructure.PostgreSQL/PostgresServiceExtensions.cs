using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Infrastructure.PostgreSQL;

public static class PostgresServiceExtensions
{
    /// <summary>
    /// Registers the PostgreSQL repository factory as both
    /// <see cref="IPostgresRepositoryFactory"/> and <see cref="IExternalRepositoryFactory"/>
    /// so the pool panel discovers it automatically.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPostgres(this IServiceCollection services)
    {
        services.AddSingleton<PostgresRepositoryFactory>();
        services.AddSingleton<IPostgresRepositoryFactory>(sp => sp.GetRequiredService<PostgresRepositoryFactory>());
        services.AddSingleton<IExternalRepositoryFactory>(sp => sp.GetRequiredService<PostgresRepositoryFactory>());
        return services;
    }
}
