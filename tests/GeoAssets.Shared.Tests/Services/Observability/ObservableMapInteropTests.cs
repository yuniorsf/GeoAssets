using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Observability;

public class ObservableMapInteropTests
{
    /// <summary>Records every call it receives so pass-through tests can assert forwarding without a mocking library.</summary>
    private sealed class FakeMapInterop : IMapInterop
    {
        public List<(string Method, object?[] Args)> Calls { get; } = [];

        private Task Record(string method, params object?[] args)
        {
            Calls.Add((method, args));
            return Task.CompletedTask;
        }

        public Task InitializeMapAsync(string divId, double lat, double lon, int zoom) => Record(nameof(InitializeMapAsync), divId, lat, lon, zoom);
        public Task DestroyMapAsync(string divId) => Record(nameof(DestroyMapAsync), divId);
        public Task InvalidateSizeAsync(string divId) => Record(nameof(InvalidateSizeAsync), divId);
        public Task SetViewAsync(string divId, double lat, double lon, int zoom) => Record(nameof(SetViewAsync), divId, lat, lon, zoom);
        public Task RenderFeatureAsync(string divId, GeoFeature feature) => Record(nameof(RenderFeatureAsync), divId, feature);
        public Task RenderAllFeaturesAsync(string divId, IEnumerable<GeoFeature> features) => Record(nameof(RenderAllFeaturesAsync), divId, features);
        public Task RenderAllFeaturesAsync(string divId, IReadOnlyList<JsonElement> features) => Record(nameof(RenderAllFeaturesAsync), divId, features);
        public Task RenderAllFeaturesRawJsonAsync(string divId, string rawFeaturesJson) => Record(nameof(RenderAllFeaturesRawJsonAsync), divId, rawFeaturesJson);
        public Task RenderFeatureBatchRawJsonAsync(string divId, string rawFeaturesJson) => Record(nameof(RenderFeatureBatchRawJsonAsync), divId, rawFeaturesJson);
        public Task RemoveFeatureAsync(string divId, string featureId) => Record(nameof(RemoveFeatureAsync), divId, featureId);
        public Task ClearAllFeaturesAsync(string divId) => Record(nameof(ClearAllFeaturesAsync), divId);
        public Task EnableDrawModeAsync(string divId, GeometryType mode) => Record(nameof(EnableDrawModeAsync), divId, mode);
        public Task DisableDrawModeAsync(string divId) => Record(nameof(DisableDrawModeAsync), divId);
        public Task SetSnapTargetLayerAsync(string divId, IReadOnlyCollection<string> allowedTargetAssetTypeIds) => Record(nameof(SetSnapTargetLayerAsync), divId, allowedTargetAssetTypeIds);
        public Task ClearSnapTargetScopeAsync(string divId) => Record(nameof(ClearSnapTargetScopeAsync), divId);
        public Task AddTileLayerAsync(string divId, string layerId, string url, TileLayerOptions? options = null) => Record(nameof(AddTileLayerAsync), divId, layerId, url, options);
        public Task RemoveTileLayerAsync(string divId, string layerId) => Record(nameof(RemoveTileLayerAsync), divId, layerId);
        public Task AddWmsLayerAsync(string divId, string layerId, string wmsBaseUrl, WmsLayerOptions options) => Record(nameof(AddWmsLayerAsync), divId, layerId, wmsBaseUrl, options);
        public Task RemoveWmsLayerAsync(string divId, string layerId) => Record(nameof(RemoveWmsLayerAsync), divId, layerId);
        public Task SetLayerVisibilityAsync(string divId, string assetTypeId, bool visible) => Record(nameof(SetLayerVisibilityAsync), divId, assetTypeId, visible);
        public Task FitBoundsAsync(string divId, double[] bbox) => Record(nameof(FitBoundsAsync), divId, bbox);
        public Task PanToFeatureAsync(string divId, string featureId) => Record(nameof(PanToFeatureAsync), divId, featureId);
        public Task HighlightFeatureAsync(string divId, string featureId) => Record(nameof(HighlightFeatureAsync), divId, featureId);
        public Task ClearHighlightAsync(string divId, string featureId) => Record(nameof(ClearHighlightAsync), divId, featureId);
        public Task RegisterEventHandlersAsync(string divId, DotNetObjectReference<object> handlerRef) => Record(nameof(RegisterEventHandlersAsync), divId, handlerRef);
    }

    private static ObservableMapInterop Sut(FakeMapInterop inner) =>
        new(inner, NullLogger<ObservableMapInterop>.Instance, TimeProvider.System);

    private static Activity? CaptureSpan(Action act)
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ImportDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured = activity
        };
        ActivitySource.AddActivityListener(listener);

        act();

        return captured;
    }

    // ── Instrumented ─────────────────────────────────────────────────────────

    [Fact]
    public void RenderAllFeaturesAsync_EmitsSpanWithFeatureCountTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var features = new List<GeoFeature> { new() { Id = "a" } };

        var captured = CaptureSpan(() => sut.RenderAllFeaturesAsync("map1", features).GetAwaiter().GetResult());

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderAllFeaturesAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_all");
        captured.GetTagItem("feature.count").Should().Be(1);
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    [Fact]
    public void RenderFeatureAsync_EmitsSpanWithFeatureIdTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var feature = new GeoFeature { Id = "pole-1" };

        var captured = CaptureSpan(() => sut.RenderFeatureAsync("map1", feature).GetAwaiter().GetResult());

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderFeatureAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_feature");
        captured.GetTagItem("feature.id").Should().Be("pole-1");
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    [Fact]
    public void RenderAllFeaturesRawJsonAsync_EmitsSpanWithPayloadBytesTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        const string rawJson = """[{"id":"a"}]""";

        var captured = CaptureSpan(() => sut.RenderAllFeaturesRawJsonAsync("map1", rawJson).GetAwaiter().GetResult());

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderAllFeaturesRawJsonAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_all_raw_json");
        captured.GetTagItem("payload.bytes").Should().Be(rawJson.Length);
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    // ── Pass-through ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeMapAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.InitializeMapAsync("map1", 1, 2, 3);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.InitializeMapAsync));
    }

    [Fact]
    public async Task DestroyMapAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.DestroyMapAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.DestroyMapAsync));
    }

    [Fact]
    public async Task InvalidateSizeAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.InvalidateSizeAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.InvalidateSizeAsync));
    }

    [Fact]
    public async Task SetViewAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.SetViewAsync("map1", 1, 2, 3);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.SetViewAsync));
    }

    [Fact]
    public async Task RenderAllFeaturesAsync_JsonElementOverload_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var features = new List<JsonElement>();

        await sut.RenderAllFeaturesAsync("map1", features);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderAllFeaturesAsync));
    }

    [Fact]
    public async Task RenderFeatureBatchRawJsonAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.RenderFeatureBatchRawJsonAsync("map1", "[]");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderFeatureBatchRawJsonAsync));
    }

    [Fact]
    public async Task RemoveFeatureAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.RemoveFeatureAsync("map1", "f1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RemoveFeatureAsync));
    }

    [Fact]
    public async Task ClearAllFeaturesAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.ClearAllFeaturesAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.ClearAllFeaturesAsync));
    }

    [Fact]
    public async Task EnableDrawModeAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.EnableDrawModeAsync("map1", GeometryType.Point);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.EnableDrawModeAsync));
    }

    [Fact]
    public async Task DisableDrawModeAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.DisableDrawModeAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.DisableDrawModeAsync));
    }

    [Fact]
    public async Task SetSnapTargetLayerAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.SetSnapTargetLayerAsync("map1", ["type-a"]);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.SetSnapTargetLayerAsync));
    }

    [Fact]
    public async Task ClearSnapTargetScopeAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.ClearSnapTargetScopeAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.ClearSnapTargetScopeAsync));
    }

    [Fact]
    public async Task AddTileLayerAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.AddTileLayerAsync("map1", "layer1", "https://tiles.example/{z}/{x}/{y}.png");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.AddTileLayerAsync));
    }

    [Fact]
    public async Task RemoveTileLayerAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.RemoveTileLayerAsync("map1", "layer1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RemoveTileLayerAsync));
    }

    [Fact]
    public async Task AddWmsLayerAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.AddWmsLayerAsync("map1", "layer1", "https://wms.example", new WmsLayerOptions());

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.AddWmsLayerAsync));
    }

    [Fact]
    public async Task RemoveWmsLayerAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.RemoveWmsLayerAsync("map1", "layer1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RemoveWmsLayerAsync));
    }

    [Fact]
    public async Task SetLayerVisibilityAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.SetLayerVisibilityAsync("map1", "type-a", true);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.SetLayerVisibilityAsync));
    }

    [Fact]
    public async Task FitBoundsAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.FitBoundsAsync("map1", [0, 0, 1, 1]);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.FitBoundsAsync));
    }

    [Fact]
    public async Task PanToFeatureAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.PanToFeatureAsync("map1", "f1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.PanToFeatureAsync));
    }

    [Fact]
    public async Task HighlightFeatureAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.HighlightFeatureAsync("map1", "f1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.HighlightFeatureAsync));
    }

    [Fact]
    public async Task ClearHighlightAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.ClearHighlightAsync("map1", "f1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.ClearHighlightAsync));
    }

    [Fact]
    public async Task RegisterEventHandlersAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        using var handlerRef = DotNetObjectReference.Create((object)new());

        await sut.RegisterEventHandlersAsync("map1", handlerRef);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RegisterEventHandlersAsync));
    }
}
