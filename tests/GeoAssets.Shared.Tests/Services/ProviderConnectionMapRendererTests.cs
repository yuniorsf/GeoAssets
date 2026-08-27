using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class ProviderConnectionMapRendererTests
{
    private class FakeAssetProvider(IReadOnlyList<GeoFeature> features) : IAssetProvider
    {
        public IReadOnlyList<GeoFeature> GetAll() => features;

        public GeoFeature? GetById(string id) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNeighbors(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetDescendants(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetAncestors(string featureId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) => throw new NotSupportedException();
        public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() => throw new NotSupportedException();
        public bool HasCycles() => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> TopologicalSort() => throw new NotSupportedException();
        public void Add(GeoFeature feature) => throw new NotSupportedException();
        public void Update(GeoFeature feature) => throw new NotSupportedException();
        public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public IReadOnlyList<AssetType> GetAssetTypes() => throw new NotSupportedException();
        public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
        public void DeleteAssetType(Guid id) => throw new NotSupportedException();

        public event EventHandler<GeoFeature>? FeatureAdded;
        public event EventHandler<GeoFeature>? FeatureUpdated;
        public event EventHandler<string>? FeatureDeleted;
        public event EventHandler? CollectionChanged;
    }

    private sealed class FakeWmsAssetProvider(IReadOnlyList<GeoFeature> features)
        : FakeAssetProvider(features), IWmsProvider
    {
        public string WmsBaseUrl => "https://wms.example.com/geoassets/wms";
        public string WmsLayerName => "geoassets:layer";
        public string WmsFormat => "image/png";
    }

    private sealed class FakeMapInterop : IMapInterop
    {
        public List<(string DivId, string LayerId, string BaseUrl, WmsLayerOptions Options)> WmsLayerCalls { get; } = [];
        public List<(string DivId, GeoFeature Feature)> RenderedFeatures { get; } = [];
        public bool ThrowOnRenderFeature { get; init; }

        public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options)
        {
            WmsLayerCalls.Add((divId, layerId, wmsBaseUrl, options));
            return Task.CompletedTask;
        }

        public Task RenderFeatureAsync(string divId, GeoFeature feature)
        {
            if (ThrowOnRenderFeature) throw new InvalidOperationException("Simulated JS interop failure.");
            RenderedFeatures.Add((divId, feature));
            return Task.CompletedTask;
        }

        public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) => throw new NotSupportedException();
        public Task DestroyMapAsync(string divId) => throw new NotSupportedException();
        public Task InvalidateSizeAsync(string divId) => throw new NotSupportedException();
        public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features) => throw new NotSupportedException();
        public Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson) => throw new NotSupportedException();
        public Task RemoveFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task ClearAllFeaturesAsync(string divId) => throw new NotSupportedException();
        public Task EnableDrawModeAsync(string divId, GeometryType mode) => throw new NotSupportedException();
        public Task DisableDrawModeAsync(string divId) => throw new NotSupportedException();
        public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => throw new NotSupportedException();
        public Task RemoveTileLayerAsync(string divId, string layerId) => throw new NotSupportedException();
        public Task RemoveWmsLayerAsync(string divId, string layerId) => throw new NotSupportedException();
        public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) => throw new NotSupportedException();
        public Task FitBoundsAsync(string divId, double[] bbox) => throw new NotSupportedException();
        public Task PanToFeatureAsync(string divId, string featureId) => throw new NotSupportedException();
        public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => throw new NotSupportedException();
    }

    private sealed class FakeMapContext(string mapDivId = "test-map") : ICurrentMapContext
    {
        public string MapDivId => mapDivId;
    }

    private static GeoFeature NewFeature() => new() { Id = Guid.NewGuid().ToString() };

    [Fact]
    public void Constructor_Subscribes_WmsEntryAdded_AddsWmsLayer()
    {
        // XD01-83 regression: fails without the fix — the pool has no EntryAdded event to
        // subscribe to, or this renderer never subscribes in its constructor.
        var pool = new ProviderPool();
        var mapInterop = new FakeMapInterop();
        var mapContext = new FakeMapContext("my-map");
        using var sut = new ProviderConnectionMapRenderer(
            pool, mapInterop, mapContext, NullLogger<ProviderConnectionMapRenderer>.Instance);

        var entry = pool.Add("WMS Layer", new FakeWmsAssetProvider([]));

        mapInterop.WmsLayerCalls.Should().ContainSingle();
        var call = mapInterop.WmsLayerCalls[0];
        call.DivId.Should().Be("my-map");
        call.LayerId.Should().Be(entry.Id.ToString());
        call.BaseUrl.Should().Be("https://wms.example.com/geoassets/wms");
        call.Options.Layers.Should().Be("geoassets:layer");
        call.Options.Format.Should().Be("image/png");
        mapInterop.RenderedFeatures.Should().BeEmpty();
    }

    [Fact]
    public void EntryAdded_NonWmsEntry_RendersEachFeature()
    {
        var pool = new ProviderPool();
        var mapInterop = new FakeMapInterop();
        var mapContext = new FakeMapContext("my-map");
        using var sut = new ProviderConnectionMapRenderer(
            pool, mapInterop, mapContext, NullLogger<ProviderConnectionMapRenderer>.Instance);

        var f1 = NewFeature();
        var f2 = NewFeature();
        var provider = new FakeAssetProvider([f1, f2]);

        pool.Add("Local", provider);

        mapInterop.WmsLayerCalls.Should().BeEmpty();
        mapInterop.RenderedFeatures.Select(r => r.Feature.Id).Should().BeEquivalentTo([f1.Id, f2.Id]);
        mapInterop.RenderedFeatures.Should().OnlyContain(r => r.DivId == "my-map");
    }

    [Fact]
    public void Dispose_UnsubscribesFromEntryAdded()
    {
        var pool = new ProviderPool();
        var mapInterop = new FakeMapInterop();
        var sut = new ProviderConnectionMapRenderer(
            pool, mapInterop, new FakeMapContext(), NullLogger<ProviderConnectionMapRenderer>.Instance);

        sut.Dispose();
        pool.Add("WMS Layer", new FakeWmsAssetProvider([]));

        mapInterop.WmsLayerCalls.Should().BeEmpty();
    }

    [Fact]
    public void EntryAdded_RenderThrows_ExceptionIsCaughtAndDoesNotPropagateToAdd()
    {
        var pool = new ProviderPool();
        var mapInterop = new FakeMapInterop { ThrowOnRenderFeature = true };
        using var sut = new ProviderConnectionMapRenderer(
            pool, mapInterop, new FakeMapContext(), NullLogger<ProviderConnectionMapRenderer>.Instance);

        var provider = new FakeAssetProvider([NewFeature()]);

        var act = () => pool.Add("Local", provider);

        act.Should().NotThrow();
    }
}
