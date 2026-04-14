using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.WMS;

public static class WmsServiceExtensions
{
    /// <summary>
    /// Registers the WMS provider plugin so the pool panel shows a WMS connection option.
    ///
    /// On the server side, call <c>app.MapWmsApi()</c> (via <c>GeoAssetsRestApiExtensions</c>)
    /// to expose the OGC WMS 1.1.1 endpoint backed by PostGIS.
    /// </summary>
    public static IServiceCollection AddGeoAssetsWms(this IServiceCollection services)
    {
        services.AddSingleton<IProviderPlugin, WmsProviderPlugin>();
        return services;
    }
}
