using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// The Web host's provider plugins (ADR-002 D2, XD01-428): the modules it bundles, named here so the trimmer keeps them
/// (<see cref="StaticAssemblyPluginSource.Add{TModule}"/>), discovered for <see cref="ProviderHostTargets.Browser"/>.
/// </summary>
internal static class WebProviderPluginExtensions
{
    /// <summary>
    /// Registers the REST, WFS, WMS and Shapefile provider plugins and the <c>ProviderPluginRegistry</c> the boot dialog
    /// and pool panel read, plus the Shapefile export/import tools the pool panel offers — not part of the Shapefile
    /// plugin module.
    /// </summary>
    public static IServiceCollection AddWebProviderPlugins(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDiscoveredProviderPlugins(
            configuration,
            ProviderHostTargets.Browser,
            new StaticAssemblyPluginSource()
                .Add<RestProviderPluginModule>()
                .Add<WfsProviderPluginModule>()
                .Add<WmsProviderPluginModule>()
                .Add<ShapefileProviderPluginModule>());
        services.AddGeoAssetsShapefileTools();
        return services;
    }
}
