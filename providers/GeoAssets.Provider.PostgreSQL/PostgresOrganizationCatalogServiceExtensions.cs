using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL;

public static class PostgresOrganizationCatalogServiceExtensions
{
    /// <summary>The key the host's shared (tenant-less) catalog is re-registered under.</summary>
    public const string SharedCatalogKey = "geoassets:shared-asset-catalog";

    /// <summary>
    /// Makes the host's <see cref="IAssetProvider"/> organization-scoped (XD01-338): a scoped
    /// registration resolving, for the request's <see cref="ITenantContext"/>, the
    /// <see cref="OrganizationAssetCatalog"/> of <see cref="PostgresOrganizationAssetCatalogs"/> —
    /// that organization's features plus the shared ones; the shared ones alone for
    /// <see cref="Guid.Empty"/> or when no <see cref="ITenantContext"/> is registered.
    ///
    /// The singleton <see cref="IAssetProvider"/> already registered (by
    /// <see cref="PostgresServiceExtensions.AddWarmedPostgresAssetProvider"/>) becomes the shared
    /// catalog: it is moved under <see cref="SharedCatalogKey"/>, still a singleton the container
    /// owns. Each organization's provider uses <paramref name="connectionString"/>, which must be
    /// the shared catalog's database. A <see cref="TimeProvider"/> is added when none is registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">No singleton <see cref="IAssetProvider"/> is registered.</exception>
    public static IServiceCollection AddPostgresOrganizationAssetCatalogs(
        this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var shared = services.LastOrDefault(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService);
        if (shared is not { Lifetime: ServiceLifetime.Singleton })
        {
            throw new InvalidOperationException(
                $"{nameof(AddPostgresOrganizationAssetCatalogs)} needs the shared catalog registered first as a singleton {nameof(IAssetProvider)} " +
                $"(call {nameof(PostgresServiceExtensions.AddWarmedPostgresAssetProvider)} before it).");
        }

        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService).ToList())
            services.Remove(descriptor);

        services.AddKeyedSingleton<IAssetProvider>(SharedCatalogKey, (sp, _) => Resolve(shared, sp));

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
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped(sp => sp.GetRequiredService<PostgresOrganizationAssetCatalogs>()
            .For(sp.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty));
        return services;
    }

    private static IAssetProvider Resolve(ServiceDescriptor descriptor, IServiceProvider sp) =>
        (IAssetProvider)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
}
