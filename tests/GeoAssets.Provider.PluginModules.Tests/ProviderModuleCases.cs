using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>The spatial providers' modules (XD01-425; <c>pmtiles</c>, XD01-438), keyed by plugin id.</summary>
internal static class ProviderModuleCases
{
    public static readonly IReadOnlyDictionary<string, ProviderModuleCase> ById =
        new[]
        {
            new ProviderModuleCase(typeof(RestProviderPluginModule), "rest", ProviderHostTargets.All,
                s => s.AddGeoAssetsRest(), typeof(RestProviderPlugin)),
            new ProviderModuleCase(typeof(WfsProviderPluginModule), "wfs", ProviderHostTargets.All,
                s => s.AddGeoAssetsWfs(), typeof(WfsProviderPlugin)),
            new ProviderModuleCase(typeof(WmsProviderPluginModule), "wms", ProviderHostTargets.All,
                s => s.AddGeoAssetsWms(), typeof(WmsProviderPlugin)),
            new ProviderModuleCase(typeof(ShapefileProviderPluginModule), "shapefile", ProviderHostTargets.All,
                s => s.AddGeoAssetsShapefilePlugin(), typeof(ShapefileProviderPlugin)),
            new ProviderModuleCase(typeof(PostgresProviderPluginModule), "postgres", ProviderHostTargets.Server,
                s => s.AddGeoAssetsPostgres(), typeof(PostgresProviderPlugin)),
            new ProviderModuleCase(typeof(PMTilesProviderPluginModule), "pmtiles", ProviderHostTargets.MobileDesktop | ProviderHostTargets.Server,
                s => s.AddGeoAssetsPMTilesPacks(), typeof(PMTilesPackProviderPlugin)),
        }.ToDictionary(c => c.PluginId, StringComparer.Ordinal);

    public static TheoryData<string> Ids => [.. ById.Keys];

    public static IProviderPluginModule Create(string pluginId) =>
        (IProviderPluginModule)Activator.CreateInstance(ById[pluginId].ModuleType)!;
}
