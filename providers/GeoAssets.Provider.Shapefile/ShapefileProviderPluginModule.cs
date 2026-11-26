using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// The Shapefile provider's <see cref="IProviderPluginModule"/> (ADR-002 D1, XD01-425): plugin id <c>shapefile</c>,
/// on every host — its provider reads an uploaded archive anywhere, and a path on disk where there is a filesystem.
/// Registers exactly what <see cref="ShapefileServiceExtensions.AddGeoAssetsShapefilePlugin"/> registers (the
/// provider plugin only), so a host gets the same services whether it calls the extension or runs this module; a host
/// that also exports/imports Shapefiles still calls <see cref="ShapefileServiceExtensions.AddGeoAssetsShapefileTools"/>
/// (XD01-424). Reads no configuration.
/// </summary>
public sealed class ShapefileProviderPluginModule : IProviderPluginModule
{
    public string PluginId => "shapefile";

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddGeoAssetsShapefilePlugin();
    }
}
