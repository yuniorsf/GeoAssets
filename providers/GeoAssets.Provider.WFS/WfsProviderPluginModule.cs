using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// The WFS provider's <see cref="IProviderPluginModule"/> (ADR-002 D1, XD01-425): plugin id <c>wfs</c>, on every
/// host — it is a plain HTTP client of an OGC WFS endpoint. Registers exactly what
/// <see cref="WfsServiceExtensions.AddGeoAssetsWfs"/> registers, so a host gets the same services whether it calls
/// the extension or runs this module. Reads no configuration.
/// </summary>
public sealed class WfsProviderPluginModule : IProviderPluginModule
{
    public string PluginId => "wfs";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsWfs();
    }
}
