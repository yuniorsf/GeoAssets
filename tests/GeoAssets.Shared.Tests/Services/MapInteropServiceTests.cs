using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Tests;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class MapInteropServiceTests
{
    private static GeoFeature Feature(string id, Guid assetTypeId, string? layerId = null, params (string key, string value)[] attributes)
    {
        var feature = new GeoFeature { Id = id, Properties = { AssetTypeId = assetTypeId.ToString() } };
        if (layerId is not null) feature.Properties.LayerId = layerId;
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    // ── No resolution possible ────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_UnknownAssetType_OmitsFeature()
    {
        var repo = new TestAssetProvider();
        var feature = Feature("a", Guid.NewGuid());

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().BeEmpty();
    }

    [Fact]
    public void BuildStyleMap_KnownAssetTypeButNothingResolves_OmitsFeature()
    {
        var repo = new TestAssetProvider();
        var assetType = new AssetType { Name = "Plain" }; // no DefaultLayerId, no rules
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().BeEmpty();
    }

    // ── Resolution present ────────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_DefaultLayerResolves_MapsFeatureToLayerStyle()
    {
        var repo = new TestAssetProvider();
        var layer = new Layer { Name = "Default", GeometryType = GeometryType.Point, Color = "#ff0000", Radius = 6 };
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().ContainKey("a");
        map["a"].Color.Should().Be("#ff0000");
        map["a"].Radius.Should().Be(6);
    }

    [Fact]
    public void BuildStyleMap_TwoFeaturesSameTypeDifferentAttributes_MatchDifferentRules()
    {
        // Reproduces the ticket's acceptance criterion directly: two features of the same
        // AssetType but with CustomAttributes matching different LayerRules resolve to
        // visibly different styles.
        var repo = new TestAssetProvider();
        var steelLayer = new Layer { Name = "Steel", Color = "#111111", Weight = 5 };
        var woodLayer = new Layer { Name = "Wood", Color = "#8b5a2b", Weight = 2 };
        repo.AddLayer(steelLayer);
        repo.AddLayer(woodLayer);

        var assetType = new AssetType { Name = "Pole" };
        repo.AddAssetType(assetType);

        repo.AddLayerRule(new LayerRule
        {
            AssetTypeId = assetType.Id,
            LayerId = steelLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        });
        repo.AddLayerRule(new LayerRule
        {
            AssetTypeId = assetType.Id,
            LayerId = woodLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "wood" }]
        });

        var steelFeature = Feature("steel-pole", assetType.Id, attributes: ("material", "steel"));
        var woodFeature = Feature("wood-pole", assetType.Id, attributes: ("material", "wood"));

        var map = MapInteropService.BuildStyleMap(repo, [steelFeature, woodFeature]);

        map["steel-pole"].Color.Should().Be("#111111");
        map["steel-pole"].Weight.Should().Be(5);
        map["wood-pole"].Color.Should().Be("#8b5a2b");
        map["wood-pole"].Weight.Should().Be(2);
        map["steel-pole"].Should().NotBe(map["wood-pole"]);
    }

    [Fact]
    public void BuildStyleMap_PerFeatureLayerIdOverride_TakesPriorityOverDefault()
    {
        var repo = new TestAssetProvider();
        var defaultLayer = new Layer { Name = "Default", Color = "#3388ff" };
        var overrideLayer = new Layer { Name = "Override", Color = "#00ff00" };
        repo.AddLayer(defaultLayer);
        repo.AddLayer(overrideLayer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = defaultLayer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id, layerId: overrideLayer.Id.ToString());

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].Color.Should().Be("#00ff00");
    }

    // ── Empty/null fields ──────────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_LayerWithNoDashArrayOrIcon_MapsToNull()
    {
        var repo = new TestAssetProvider();
        var layer = new Layer { Name = "Plain" }; // DashArray/IconUrl left at their empty defaults
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].DashArray.Should().BeNull();
        map["a"].IconUrl.Should().BeNull();
    }

    [Fact]
    public void BuildStyleMap_LayerWithDashArrayAndIcon_MapsThemThrough()
    {
        var repo = new TestAssetProvider();
        var layer = new Layer { Name = "Line", DashArray = "5, 5", IconUrl = "/icons/pole.png" };
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Wire", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].DashArray.Should().Be("5, 5");
        map["a"].IconUrl.Should().Be("/icons/pole.png");
    }

    // ── No regression fallback ────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_MultipleFeatures_OnlyResolvedOnesAppearInMap()
    {
        var repo = new TestAssetProvider();
        var layer = new Layer { Name = "Default", Color = "#123456" };
        repo.AddLayer(layer);
        var withDefault = new AssetType { Name = "HasDefault", DefaultLayerId = layer.Id };
        var withoutDefault = new AssetType { Name = "NoDefault" };
        repo.AddAssetType(withDefault);
        repo.AddAssetType(withoutDefault);

        var resolved = Feature("resolved", withDefault.Id);
        var unresolved = Feature("unresolved", withoutDefault.Id);

        var map = MapInteropService.BuildStyleMap(repo, [resolved, unresolved]);

        map.Should().ContainKey("resolved");
        map.Should().NotContainKey("unresolved"); // geoassets.js falls back to colorMap/default for this one
    }

    // ── Feature states (XD01-309) ─────────────────────────────────────────────

    /// <summary>Records every JS call so the interop call shape can be asserted without a browser.</summary>
    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static MapInteropService Sut(RecordingJsRuntime js) =>
        new(js, new TestAssetProvider(), Options.Create(new MapInteropOptions()));

    private static string[] Ids(int count) => [.. Enumerable.Range(0, count).Select(i => $"f{i}")];

    [Theory]
    [InlineData(MapFeatureState.Default, "default")]
    [InlineData(MapFeatureState.Selected, "selected")]
    [InlineData(MapFeatureState.Highlighted, "highlighted")]
    [InlineData(MapFeatureState.Disabled, "disabled")]
    [InlineData(MapFeatureState.Conflict, "conflict")]
    public async Task SetFeatureStatesAsync_CallsSetFeatureStatesWithDivIdIdsAndJsStateName(MapFeatureState state, string expectedName)
    {
        var js = new RecordingJsRuntime();

        await Sut(js).SetFeatureStatesAsync("map1", ["a", "b"], state);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.setFeatureStates");
        call.Args.Should().HaveCount(3);
        call.Args[0].Should().Be("map1");
        call.Args[1].Should().BeAssignableTo<IEnumerable<string>>().Which.Should().Equal("a", "b");
        call.Args[2].Should().Be(expectedName);
    }

    [Theory]
    [InlineData(MapSelectionTool.None, "none")]
    [InlineData(MapSelectionTool.ClickToggle, "click")]
    [InlineData(MapSelectionTool.Box, "box")]
    [InlineData(MapSelectionTool.Fence, "fence")]
    public async Task SetSelectionToolAsync_CallsSetSelectionToolWithDivIdAndJsToolName(MapSelectionTool tool, string expectedName)
    {
        var js = new RecordingJsRuntime();

        await Sut(js).SetSelectionToolAsync("map1", tool);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.setSelectionTool");
        call.Args.Should().Equal("map1", expectedName);
    }

    [Fact]
    public async Task SetSelectionToolAsync_UndefinedTool_ThrowsAndMakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).SetSelectionToolAsync("map1", (MapSelectionTool)99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFeatureStatesAsync_NullIds_ThrowsAndMakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).SetFeatureStatesAsync("map1", null!, MapFeatureState.Selected);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("featureIds");
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFeatureStatesAsync_EmptyIds_MakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        await Sut(js).SetFeatureStatesAsync("map1", [], MapFeatureState.Selected);

        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetFeatureStatesAsync_UndefinedState_ThrowsAndMakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).SetFeatureStatesAsync("map1", ["a"], (MapFeatureState)999);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        js.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(2000, new[] { 2000 })]
    [InlineData(2001, new[] { 2000, 1 })]
    [InlineData(4500, new[] { 2000, 2000, 500 })]
    public async Task SetFeatureStatesAsync_BatchesTwoThousandIdsPerJsCallInOrder(int idCount, int[] expectedBatchSizes)
    {
        var js = new RecordingJsRuntime();
        var ids = Ids(idCount);

        await Sut(js).SetFeatureStatesAsync("map1", ids, MapFeatureState.Selected);

        MapInteropService.FeatureStateBatchSize.Should().Be(2000);
        js.Calls.Should().OnlyContain(c => c.Identifier == "GeoAssets.setFeatureStates" && (string?)c.Args[2] == "selected");
        var batches = js.Calls.Select(c => (string[])c.Args[1]!).ToList();
        batches.Select(b => b.Length).Should().Equal(expectedBatchSizes);
        batches.SelectMany(b => b).Should().Equal(ids);
    }

    [Fact]
    public async Task ClearFeatureStatesAsync_CallsClearFeatureStatesWithDivId()
    {
        var js = new RecordingJsRuntime();

        await Sut(js).ClearFeatureStatesAsync("map1");

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.clearFeatureStates");
        call.Args.Should().Equal("map1");
    }

    [Fact]
    public void ToJsName_UndefinedState_Throws()
    {
        var act = () => ((MapFeatureState)999).ToJsName();

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("state");
    }

    // ── Zoom range (XD01-311) ────────────────────────────────────────────────

    [Theory]
    [InlineData(13, 15)]
    [InlineData(0, 24)]
    [InlineData(15, null)]
    [InlineData(null, 13)]
    [InlineData(null, null)]
    public async Task SetLayerZoomRangeAsync_CallsSetLayerZoomRangeWithDivIdTypeAndBothBounds(int? min, int? max)
    {
        var js = new RecordingJsRuntime();

        await Sut(js).SetLayerZoomRangeAsync("map1", "type-a", min, max);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.setLayerZoomRange");
        call.Args.Should().Equal("map1", "type-a", min, max);
    }

    [Theory]
    [InlineData(-1, null, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(25, null, AssetTypeZoomRangeDefect.MinZoomOutOfBounds)]
    [InlineData(null, -1, AssetTypeZoomRangeDefect.MaxZoomOutOfBounds)]
    [InlineData(null, 25, AssetTypeZoomRangeDefect.MaxZoomOutOfBounds)]
    [InlineData(16, 15, AssetTypeZoomRangeDefect.MinZoomAboveMaxZoom)]
    public async Task SetLayerZoomRangeAsync_InvalidRange_ThrowsAndMakesNoJsCall(int? min, int? max, AssetTypeZoomRangeDefect expected)
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).SetLayerZoomRangeAsync("map1", "type-a", min, max);

        (await act.Should().ThrowAsync<InvalidAssetTypeZoomRangeException>()).Which.Defect.Should().Be(expected);
        js.Calls.Should().BeEmpty();
    }

    // ── Selection fence outline (XD01-311) ───────────────────────────────────

    [Fact]
    public async Task ShowSelectionOutlineAsync_CallsShowSelectionOutlineWithDivIdAndLonLatRings()
    {
        var js = new RecordingJsRuntime();
        double[][][] rings = [[[-46.7, -23.6], [-46.6, -23.6], [-46.6, -23.5], [-46.7, -23.6]]];

        await Sut(js).ShowSelectionOutlineAsync("map1", rings);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.showSelectionOutline");
        call.Args.Should().HaveCount(2);
        call.Args[0].Should().Be("map1");
        call.Args[1].Should().BeSameAs(rings);
    }

    [Fact]
    public async Task ShowSelectionOutlineAsync_NullRings_ThrowsAndMakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).ShowSelectionOutlineAsync("map1", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("rings");
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearSelectionOutlineAsync_CallsClearSelectionOutlineWithDivId()
    {
        var js = new RecordingJsRuntime();

        await Sut(js).ClearSelectionOutlineAsync("map1");

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.clearSelectionOutline");
        call.Args.Should().Equal("map1");
    }
}
