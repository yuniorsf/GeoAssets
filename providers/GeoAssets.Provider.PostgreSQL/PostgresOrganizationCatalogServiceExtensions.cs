using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresOrganizationCatalogServiceExtensions
{
    /// <summary>The key the host's shared (tenant-less) catalog is re-registered under (<see cref="OrganizationCatalogKeys.SharedCatalog"/>).</summary>
    public const string SharedCatalogKey = OrganizationCatalogKeys.SharedCatalog;

    /// <summary>
    /// Makes the host's <see cref="IAssetProvider"/> organization-scoped natively (XD01-338): the singleton
    /// <see cref="IAssetProvider"/> already registered becomes the shared catalog (moved under <see cref="SharedCatalogKey"/>,
    /// <see cref="OrganizationCatalogServiceCollectionExtensions.MoveSharedAssetCatalogToKey"/>), and
    /// <see cref="PostgresOrganizationAssetCatalogs"/> — one cache per organization, its writes reporting their dirty tiles to
    /// the host's <see cref="ITileInvalidation"/> when it registers one (XD01-432) — is registered as the host's
    /// <see cref="IOrganizationCatalogFactory"/> (XD01-373). Each request's <see cref="IAssetProvider"/> is the factory's
    /// catalog for the request's <see cref="ITenantContext"/>: that organization's features plus the shared ones; the shared
    /// ones alone for <see cref="Guid.Empty"/> or when no <see cref="ITenantContext"/> is registered. Each organization's
    /// provider uses <paramref name="connectionString"/>, which must be the shared catalog's database. A
    /// <see cref="TimeProvider"/> is added when none is registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">No singleton <see cref="IAssetProvider"/> is registered.</exception>
    public static IServiceCollection AddPostgresOrganizationAssetCatalogs(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.MoveSharedAssetCatalogToKey();

        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .EnableRetryOnFailure())
            .Options;

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new PostgresOrganizationAssetCatalogs(
            options,
            sp.GetRequiredKeyedService<IAssetProvider>(SharedCatalogKey),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ITileInvalidation>()));
        services.AddSingleton<IOrganizationCatalogFactory>(sp => sp.GetRequiredService<PostgresOrganizationAssetCatalogs>());
        services.AddScoped(sp => sp.GetRequiredService<IOrganizationCatalogFactory>()
            .GetForOrganization(sp.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty));
        return services;
    }
}
