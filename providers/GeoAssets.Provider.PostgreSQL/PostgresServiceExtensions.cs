using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresServiceExtensions
{
    /// <summary>
    /// Registers the PostgreSQL repository factory as both
    /// <see cref="IPostgresProviderFactory"/> and <see cref="IExternalProviderFactory"/>
    /// so the pool panel discovers it automatically.
    /// </summary>
    public static IServiceCollection AddGeoAssetsPostgres(this IServiceCollection services)
    {
        services.AddSingleton<PostgresProviderFactory>();
        services.AddSingleton<IPostgresProviderFactory>(sp => sp.GetRequiredService<PostgresProviderFactory>());
        services.AddSingleton<IExternalProviderFactory>(sp => sp.GetRequiredService<PostgresProviderFactory>());
        return services;
    }
}
