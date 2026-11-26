using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.WMS;

/// <summary>
/// The WMS provider's <see cref="IProviderPluginModule"/> (ADR-002 D1, XD01-425): plugin id <c>wms</c>, on every
/// host — its provider only describes a remote OGC WMS endpoint, whose tiles the map renders. Registers exactly what
/// <see cref="WmsServiceExtensions.AddGeoAssetsWms"/> registers, so a host gets the same services whether it calls
/// the extension or runs this module. Reads no configuration.
/// </summary>
public sealed class WmsProviderPluginModule : IProviderPluginModule
{
    public string PluginId => "wms";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsWms();
    }
}
