using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PMTiles.Packs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PMTiles;

/// <summary>
/// The PMTiles provider's <see cref="IProviderPluginModule"/> (ADR-002 D1/D2, XD01-438): plugin id <c>pmtiles</c>, for
/// MAUI mobile/desktop and servers — hosts with a filesystem to keep an offline map pack on; the browser has none. Pure
/// .NET (no native code), so MAUI bundles it in its <c>StaticAssemblyPluginSource</c> where the GDAL plugin can't go.
/// Registers exactly what <see cref="PMTilesServiceExtensions.AddGeoAssetsPMTilesPacks"/> registers — the
/// <see cref="PMTilesPackProviderPlugin"/> — and reads no configuration; where packs are kept is the host's own
/// <see cref="PMTilesServiceExtensions.AddPMTilesPackStorage"/> call.
/// </summary>
public sealed class PMTilesProviderPluginModule : IProviderPluginModule
{
    public string PluginId => PMTilesPackProviderPlugin.PluginId;

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.MobileDesktop | ProviderHostTargets.Server;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsPMTilesPacks();
    }
}
