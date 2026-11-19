using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Core.Providers;

/// <summary>Registration helpers shared by every organization-catalog wiring (XD01-373).</summary>
public static class OrganizationCatalogServiceCollectionExtensions
{
    /// <summary>
    /// Moves the host's singleton <see cref="IAssetProvider"/> (the shared catalog) under
    /// <see cref="OrganizationCatalogKeys.SharedCatalog"/>, still a singleton the container owns, leaving no un-keyed
    /// <see cref="IAssetProvider"/> registered — so the caller can register each request's organization view in its place.
    /// </summary>
    /// <exception cref="InvalidOperationException">No singleton <see cref="IAssetProvider"/> is registered.</exception>
    public static IServiceCollection MoveSharedAssetCatalogToKey(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var shared = services.LastOrDefault(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService);
        if (shared is not { Lifetime: ServiceLifetime.Singleton })
        {
            throw new InvalidOperationException(
                $"Organization-scoped catalogs need the shared catalog registered first as a singleton {nameof(IAssetProvider)}.");
        }

        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IAssetProvider) && !d.IsKeyedService).ToList())
            services.Remove(descriptor);

        services.AddKeyedSingleton<IAssetProvider>(OrganizationCatalogKeys.SharedCatalog, (sp, _) => Resolve(shared, sp));
        return services;
    }

    private static IAssetProvider Resolve(ServiceDescriptor descriptor, IServiceProvider sp) =>
        (IAssetProvider)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(sp)
            ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
}
