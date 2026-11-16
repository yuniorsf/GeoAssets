using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Observability;

public class ObservableMapInteropTests
{
    /// <summary>Records every call it receives so pass-through tests can assert forwarding without a mocking library.</summary>
    private sealed class FakeMapInterop : IMapInterop, IMapSelectionInterop
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
        public Task RenderFeatureBatchAsync(string divId, IEnumerable<GeoFeature> features) => Record(nameof(RenderFeatureBatchAsync), divId, features);
        public Task RenderFeatureBatchAsync(string divId, IReadOnlyList<JsonElement> features) => Record(nameof(RenderFeatureBatchAsync), divId, features);
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
        public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state) => Record(nameof(SetFeatureStatesAsync), divId, featureIds, state);
        public Task ClearFeatureStatesAsync(string divId) => Record(nameof(ClearFeatureStatesAsync), divId);
        public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) => Record(nameof(SetSelectionToolAsync), divId, tool);
    }

    private static ObservableMapInterop Sut(FakeMapInterop inner, MapRenderMode renderMode = MapRenderMode.Leaflet) =>
        new(inner, inner, renderMode, NullLogger<ObservableMapInterop>.Instance, TimeProvider.System);

    /// <summary>
    /// An <see cref="ActivityListener"/> is process-wide, so it also sees spans that tests running
    /// in parallel emit on the same source. Each capture therefore runs under its own root
    /// activity and keeps only spans belonging to that trace.
    /// </summary>
    private static ActivityListener ListenForTrace(ActivityTraceId traceId, List<Activity> captured)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, ImportDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                    lock (captured) captured.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task<IReadOnlyList<Activity>> CaptureSpansAsync(Func<Task> act)
    {
        using var root = new Activity("test-root").Start();
        var captured = new List<Activity>();
        using var listener = ListenForTrace(root.TraceId, captured);

        await act();

        return captured;
    }

    // ── Instrumented ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderAllFeaturesAsync_EmitsSpanWithFeatureCountTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var features = new List<GeoFeature> { new() { Id = "a" } };

        var captured = (await CaptureSpansAsync(() => sut.RenderAllFeaturesAsync("map1", features))).SingleOrDefault();

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderAllFeaturesAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_all");
        captured.GetTagItem("feature.count").Should().Be(1);
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    [Fact]
    public async Task RenderFeatureAsync_EmitsSpanWithFeatureIdTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var feature = new GeoFeature { Id = "pole-1" };

        var captured = (await CaptureSpansAsync(() => sut.RenderFeatureAsync("map1", feature))).SingleOrDefault();

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderFeatureAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_feature");
        captured.GetTagItem("feature.id").Should().Be("pole-1");
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    [Fact]
    public async Task RenderAllFeaturesRawJsonAsync_EmitsSpanWithPayloadBytesTag()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        const string rawJson = """[{"id":"a"}]""";

        var captured = (await CaptureSpansAsync(() => sut.RenderAllFeaturesRawJsonAsync("map1", rawJson))).SingleOrDefault();

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderAllFeaturesRawJsonAsync));
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("map.render_all_raw_json");
        captured.GetTagItem("payload.bytes").Should().Be(rawJson.Length);
        captured.GetTagItem("map.div_id").Should().Be("map1");
    }

    [Theory]
    [InlineData(MapFeatureState.Selected, MapRenderMode.Leaflet, "selected", "leaflet")]
    [InlineData(MapFeatureState.Conflict, MapRenderMode.Canvas, "conflict", "canvas")]
    [InlineData(MapFeatureState.Default, MapRenderMode.WebGL, "default", "webgl")]
    public async Task SetFeatureStatesAsync_EmitsSpanWithCountStateAndRenderModeTags(
        MapFeatureState state, MapRenderMode renderMode, string expectedState, string expectedMode)
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner, renderMode);
        string[] ids = ["pole-1", "pole-2", "pole-3"];

        var spans = await CaptureSpansAsync(() => sut.SetFeatureStatesAsync("map1", ids, state));

        var call = inner.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(nameof(IMapSelectionInterop.SetFeatureStatesAsync));
        call.Args.Should().Equal("map1", ids, state);
        var span = spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be("Map.SetFeatureStates");
        span.GetTagItem("map.feature_count").Should().Be(3);
        span.GetTagItem("map.feature_state").Should().Be(expectedState);
        span.GetTagItem("map.render_mode").Should().Be(expectedMode);
    }

    [Fact]
    public async Task SetFeatureStatesAsync_NeverTagsFeatureIds()
    {
        var sut = Sut(new FakeMapInterop());

        var spans = await CaptureSpansAsync(() => sut.SetFeatureStatesAsync("map1", ["pole-secret-1"], MapFeatureState.Selected));

        spans.Should().ContainSingle().Which.TagObjects
            .Should().NotContain(tag => (tag.Value as string ?? string.Empty).Contains("pole-secret-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetFeatureStatesAsync_NullIds_ThrowsWithoutCallingInnerOrEmittingSpan()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        Exception? thrown = null;

        var spans = await CaptureSpansAsync(async () =>
            thrown = await Record.ExceptionAsync(() => sut.SetFeatureStatesAsync("map1", null!, MapFeatureState.Selected)));

        thrown.Should().BeOfType<ArgumentNullException>();
        inner.Calls.Should().BeEmpty();
        spans.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFeatureStatesAsync_EmptyIds_ForwardsWithoutEmittingSpan()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        var spans = await CaptureSpansAsync(() => sut.SetFeatureStatesAsync("map1", [], MapFeatureState.Selected));

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapSelectionInterop.SetFeatureStatesAsync));
        spans.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFeatureStatesAsync_InnerFailure_MarksSpanAsErrorAndRethrows()
    {
        var sut = new ObservableMapInterop(
            new FakeMapInterop(), new ThrowingSelectionInterop(), MapRenderMode.Leaflet,
            NullLogger<ObservableMapInterop>.Instance, TimeProvider.System);
        Exception? thrown = null;

        var spans = await CaptureSpansAsync(async () =>
            thrown = await Record.ExceptionAsync(() => sut.SetFeatureStatesAsync("map1", ["a"], MapFeatureState.Selected)));

        thrown.Should().BeOfType<JSException>();
        spans.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    private sealed class ThrowingSelectionInterop : IMapSelectionInterop
    {
        public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state) =>
            Task.FromException(new JSException("boom"));
        public Task ClearFeatureStatesAsync(string divId) => Task.CompletedTask;
        public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) =>
            Task.FromException(new JSException("boom"));
    }

    [Theory]
    [InlineData(MapSelectionTool.None, MapRenderMode.Leaflet, "none", "leaflet")]
    [InlineData(MapSelectionTool.ClickToggle, MapRenderMode.Canvas, "click", "canvas")]
    [InlineData(MapSelectionTool.Box, MapRenderMode.WebGL, "box", "webgl")]
    [InlineData(MapSelectionTool.Fence, MapRenderMode.Leaflet, "fence", "leaflet")]
    public async Task SetSelectionToolAsync_ForwardsToInner_AndEmitsSpanWithToolAndRenderModeTags(
        MapSelectionTool tool, MapRenderMode renderMode, string expectedTool, string expectedMode)
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner, renderMode);

        var spans = await CaptureSpansAsync(() => sut.SetSelectionToolAsync("map1", tool));

        var call = inner.Calls.Should().ContainSingle().Subject;
        call.Method.Should().Be(nameof(IMapSelectionInterop.SetSelectionToolAsync));
        call.Args.Should().Equal("map1", tool);
        var span = spans.Should().ContainSingle().Subject;
        span.OperationName.Should().Be(ObservableMapInterop.SetSelectionToolSpanName).And.Be("Map.SetSelectionTool");
        span.GetTagItem("map.selection_tool").Should().Be(expectedTool);
        span.GetTagItem("map.render_mode").Should().Be(expectedMode);
        span.GetTagItem("duration_ms").Should().NotBeNull();
        span.TagObjects.Select(t => t.Key).Should().NotContain("map.div_id");
    }

    [Fact]
    public async Task SetSelectionToolAsync_InnerFailure_MarksSpanAsErrorAndRethrows()
    {
        var sut = new ObservableMapInterop(
            new FakeMapInterop(), new ThrowingSelectionInterop(), MapRenderMode.Leaflet,
            NullLogger<ObservableMapInterop>.Instance, TimeProvider.System);
        Exception? thrown = null;

        var spans = await CaptureSpansAsync(async () =>
            thrown = await Record.ExceptionAsync(() => sut.SetSelectionToolAsync("map1", MapSelectionTool.Box)));

        thrown.Should().BeOfType<JSException>();
        spans.Should().ContainSingle().Which.Status.Should().Be(ActivityStatusCode.Error);
    }

    // ── Pass-through ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearFeatureStatesAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);

        await sut.ClearFeatureStatesAsync("map1");

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapSelectionInterop.ClearFeatureStatesAsync))
            .Which.Args.Should().Equal("map1");
    }

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
    public async Task RenderFeatureBatchAsync_DelegatesToInner()
    {
        var inner = new FakeMapInterop();
        var sut = Sut(inner);
        var features = new List<GeoFeature> { new() { Id = "a" } };

        await sut.RenderFeatureBatchAsync("map1", features);

        inner.Calls.Should().ContainSingle(c => c.Method == nameof(IMapInterop.RenderFeatureBatchAsync));
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
