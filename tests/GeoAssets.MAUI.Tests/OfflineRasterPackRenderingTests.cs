using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PMTiles.Packs;
using GeoAssets.Provider.PMTiles.Tests;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.MapTiles;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-465 — an offline pack connected as MAUI connects it (the <c>pmtiles</c> plugin over the app's pack storage) and
/// handed to the map's renderers: a raster pack is taken by <see cref="LocalRasterTileMapLayerRenderer"/> by its
/// capability — not the vector, WMS or any other renderer — and its tiles reach the map's JS reader from the stored
/// pack; a vector pack still goes to <see cref="VectorFeatureMapLayerRenderer"/>, as before.
/// </summary>
public sealed class OfflineRasterPackRenderingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "geoassets-maui-raster-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _host;
    private readonly RecordingTiles _tiles = new();

    public OfflineRasterPackRenderingTests()
    {
        Directory.CreateDirectory(_directory);
        var services = new ServiceCollection();
        services.AddGeoAssetsPMTilesPacks();
        services.AddPMTilesPackStorage(Path.Combine(_directory, "offline-packs"));
        _host = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Task<IAssetProvider> ConnectAsync(string path)
    {
        var plugin = _host.GetServices<IProviderPlugin>().Single(p => string.Equals(p.Id, PMTilesPackProviderPlugin.PluginId, StringComparison.Ordinal));
        return plugin.CreateAsync(new ProviderConfig(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PMTilesPackProviderPlugin.PackPathKey] = path,
        }), _host);
    }

    // The renderers' CanRender read capabilities only; the map interop is never reached by these tests.
    private LocalRasterTileMapLayerRenderer LocalRenderer() => new(null!, _tiles);

    [Fact]
    public async Task ARasterPack_IsDrawnByTheLocalRasterRenderer_FromTheStoredPack()
    {
        var tile = new TileAddress(1, 1, 0);
        var picked = TestRasterTiles.Archive(PMTilesTileType.Webp, [tile], bounds: (0, 0, 0, 0)).WriteTo(_directory);
        var provider = await ConnectAsync(picked);
        using var disposable = (IDisposable)provider;
        var entry = new ProviderEntry { Name = "Basemap", Provider = provider };

        LocalRenderer().CanRender(provider).Should().BeTrue();
        new VectorFeatureMapLayerRenderer(null!, null!).CanRender(provider).Should().BeFalse("a raster pack has no features to draw");
        new WmsMapLayerRenderer(null!).CanRender(provider).Should().BeFalse();

        await LocalRenderer().RenderAsync(MapLayerRenderRequest.For("map", entry));

        var (layerId, tiles, layer) = _tiles.Added.Should().ContainSingle().Subject;
        layerId.Should().Be(entry.Id.ToString());
        layer.ContentType.Should().Be("image/webp");
        var reader = new LocalRasterTileReader(tiles, layer);
        (await reader.GetTileFromJsAsync(1, 1, 0)).Should().Equal(TestRasterTiles.Webp);
        (await reader.GetTileFromJsAsync(1, 0, 0)).Should().BeNull();
        ((PMTilesRasterPackAssetProvider)provider).Pack.Path.Should().StartWith(Path.Combine(_directory, "offline-packs"), "the tiles are read from the imported copy");
    }

    [Fact]
    public async Task AVectorPack_IsStillDrawnAsFeatures()
    {
        var picked = TestPacks.Write(_directory, [new TileAddress(0, 0, 0)], maxZoom: 0);
        var provider = await ConnectAsync(picked);
        using var disposable = (IDisposable)provider;

        provider.Should().BeOfType<PMTilesPackAssetProvider>();
        new VectorFeatureMapLayerRenderer(null!, null!).CanRender(provider).Should().BeTrue();
        LocalRenderer().CanRender(provider).Should().BeFalse();
        provider.GetAll().Should().ContainSingle();
    }

    private sealed class RecordingTiles : IMapLocalTileLayerInterop
    {
        public List<(string LayerId, IRasterTileSource Tiles, LocalRasterTileLayer Layer)> Added { get; } = [];

        public Task AddLocalRasterTileLayerAsync(string divId, string layerId, IRasterTileSource tiles, LocalRasterTileLayer layer)
        {
            Added.Add((layerId, tiles, layer));
            return Task.CompletedTask;
        }

        public Task RemoveLocalRasterTileLayerAsync(string divId, string layerId) => Task.CompletedTask;
    }
}
