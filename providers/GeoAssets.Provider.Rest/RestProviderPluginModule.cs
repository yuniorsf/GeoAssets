using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// The REST provider's <see cref="IProviderPluginModule"/> (ADR-002 D1, XD01-425): plugin id <c>rest</c>, on every
/// host — it is a plain HTTP client of a GeoAssets REST API. Registers exactly what
/// <see cref="RestServiceExtensions.AddGeoAssetsRest"/> registers, so a host gets the same services whether it calls
/// the extension or runs this module. Reads no configuration.
/// </summary>
public sealed class RestProviderPluginModule : IProviderPluginModule
{
    public string PluginId => "rest";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsRest();
    }
}
