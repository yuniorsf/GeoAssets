using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.WFS;

public static class WfsServiceExtensions
{
    /// <summary>
    /// Registers the WFS provider factory and plugin so the pool panel shows a
    /// WFS connection option and the boot dialog can create WFS-backed collections.
    ///
    /// On the server side, call <c>app.MapWfsApi()</c> (from
    /// <c>GeoAssets.Server</c>) to expose the OGC WFS 2.0 endpoint backed by PostGIS.
    /// </summary>
    public static IServiceCollection AddGeoAssetsWfs(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<WfsProviderFactory>();
        services.AddSingleton<IExternalProviderFactory>(
            sp => sp.GetRequiredService<WfsProviderFactory>());
        services.AddSingleton<IProviderPlugin, WfsProviderPlugin>();
        return services;
    }
}
