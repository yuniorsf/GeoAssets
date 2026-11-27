using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresTileVersionServiceExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresTileVersionStore"/> over <paramref name="connectionString"/> (the GeoAssets database,
    /// migrated with <c>AddTileVersions</c>) as the host's <see cref="ITileVersionStore"/> (XD01-458), singleton, unless one
    /// is registered.
    /// </summary>
    public static IServiceCollection AddPostgresTileVersionStore(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton<ITileVersionStore>(new PostgresTileVersionStore(connectionString));
        return services;
    }
}
