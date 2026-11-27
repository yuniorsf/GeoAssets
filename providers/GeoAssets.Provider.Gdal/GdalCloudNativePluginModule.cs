using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.Cog;
using GeoAssets.Provider.Gdal.PMTiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.Gdal;

/// <summary>
/// The GDAL plugin's <see cref="IProviderPluginModule"/> (ADR-002 D1/D5, XD01-437): plugin id <c>gdal-cloud-native</c>,
/// for servers and MAUI mobile/desktop. It registers no <see cref="IProviderPlugin"/> — its ports are formats and tile
/// sources, all shared contracts of <c>GeoAssets.Core</c>:
/// <list type="bullet">
/// <item>everywhere: a keyed <see cref="IVectorTileSource"/> (<see cref="PMTilesVectorTileSource"/>, pure .NET) per
/// configured PMTiles archive;</item>
/// <item>where the native GDAL runtime exists (<see cref="GdalNativeRuntime.IsSupportedPlatform"/> — macOS and Linux,
/// x64/arm64): <see cref="IFileGeodatabaseExportService"/>, <see cref="IVectorFileReader"/> (FlatGeobuf, GeoParquet) and
/// a keyed <see cref="IRasterTileSource"/> (<see cref="CogRasterTileSource"/>) per configured COG. Elsewhere (iOS,
/// Android, Windows) they are left out, and a configured COG fails the startup instead of every tile.</item>
/// </list>
/// The tile sources are keyed by their configured id (<see cref="GdalPluginOptions"/>) so they never replace the host's
/// own unkeyed sources. Registering touches no native code: GDAL is configured on first use
/// (<see cref="GdalNativeInitializer"/>).
/// </summary>
public sealed class GdalCloudNativePluginModule : IProviderPluginModule
{
    /// <summary>The module's plugin id.</summary>
    public const string Id = "gdal-cloud-native";

    private readonly bool _nativeRuntimeAvailable;

    public GdalCloudNativePluginModule()
        : this(GdalNativeRuntime.IsSupportedPlatform)
    {
    }

    internal GdalCloudNativePluginModule(bool nativeRuntimeAvailable) => _nativeRuntimeAvailable = nativeRuntimeAvailable;

    public string PluginId => Id;

    public ProviderHostTargets SupportedHosts => ProviderHostTargets.Server | ProviderHostTargets.MobileDesktop;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = GdalPluginOptions.Read(configuration);
        foreach (var archive in options.PMTilesArchives)
            services.AddKeyedSingleton<IVectorTileSource>(archive.Id, (_, _) => new PMTilesVectorTileSource(archive.Path));

        if (!_nativeRuntimeAvailable)
        {
            if (options.CogRasters.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{GdalPluginOptions.SectionName}:Cog' configures {options.CogRasters.Count} raster(s), but this platform " +
                    $"({GdalNativeRuntime.Describe()}) has no native GDAL runtime in the plugin.");
            }

            return;
        }

        services.AddGeoAssetsFileGeodatabaseExport();
        services.TryAddSingleton<IVectorFileReader, OgrVectorFileReader>();
        foreach (var raster in options.CogRasters)
            services.AddKeyedSingleton<IRasterTileSource>(raster.Id, (_, _) => new CogRasterTileSource(raster.Id, raster.Path));
    }
}
