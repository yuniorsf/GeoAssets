using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Catalogs;

/// <summary>The server's catalog pool in the host (XD01-371).</summary>
public static class CatalogPoolHostExtensions
{
    /// <summary>
    /// Registers the <see cref="CatalogPool"/> of <see cref="CatalogConfiguration.ProvidersKey"/> as
    /// <see cref="ICatalogPool"/>, and its default catalog as the host's singleton <see cref="IAssetProvider"/> — which a
    /// module may then scope per request (e.g. organization catalogs). Call before enabling modules; the plugins they
    /// register are looked up when the pool is initialized (<see cref="InitializeCatalogPoolAsync"/>).
    /// </summary>
    public static IServiceCollection AddServerCatalogPool(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        var definitions = CatalogConfiguration.Read(configuration);
        services.TryAddSingleton<ProviderPluginRegistry>();
        services.AddSingleton(sp => new CatalogPool(
            definitions, sp.GetRequiredService<ProviderPluginRegistry>(), sp.GetRequiredService<ILogger<CatalogPool>>()));
        services.AddSingleton<ICatalogPool>(sp => sp.GetRequiredService<CatalogPool>());
        services.AddSingleton<IAssetProvider>(sp => sp.GetRequiredService<ICatalogPool>().DefaultCatalog);
        return services;
    }

    /// <summary>Builds every configured catalog. Call once, right after the host is built and before anything reads assets.</summary>
    public static Task InitializeCatalogPoolAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<CatalogPool>().InitializeAsync(services, cancellationToken);
    }
}
