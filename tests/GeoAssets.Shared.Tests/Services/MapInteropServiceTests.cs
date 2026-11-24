using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Tests;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Text.RegularExpressions;
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

        /// <summary>What every call answers (the default of the asked type when null).</summary>
        public object? Result { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(Result is TValue value ? value : default!);
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

    // ── Reference layers of another organization (XD01-331) ──────────────────

    [Fact]
    public async Task SetReferenceLayerAsync_CallsSetReferenceLayerWithTheRawJsonUnchangedAndTheOptions()
    {
        var js = new RecordingJsRuntime();
        const string json = """[{"id":"p1","geometry":{"type":"Point","coordinates":[-38.5,-12.9]},"label":"Pole 1","assetTypeId":"t","assetTypeName":"Pole"}]""";
        var options = new ReferenceLayerOptions(LabelMinZoom: 16);

        await Sut(js).SetReferenceLayerAsync("map1", "reference-coelba", json, options);

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.setReferenceLayer");
        call.Args.Should().HaveCount(4);
        call.Args[0].Should().Be("map1");
        call.Args[1].Should().Be("reference-coelba");
        call.Args[2].Should().BeSameAs(json, "the body is forwarded without a .NET parse");
        call.Args[3].Should().BeSameAs(options);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task SetReferenceLayerAsync_LabelZoomOutsideTheMapsZooms_ThrowsAndMakesNoJsCall(int labelMinZoom)
    {
        var js = new RecordingJsRuntime();

        var act = () => Sut(js).SetReferenceLayerAsync("map1", "reference-a", "[]", new ReferenceLayerOptions(labelMinZoom));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("options");
        js.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    public async Task SetReferenceLayerAsync_LabelZoomAtTheBounds_IsAccepted(int labelMinZoom)
    {
        var js = new RecordingJsRuntime();

        await Sut(js).SetReferenceLayerAsync("map1", "reference-a", "[]", new ReferenceLayerOptions(labelMinZoom));

        js.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task SetReferenceLayerAsync_MissingArguments_ThrowAndMakeNoJsCall()
    {
        var js = new RecordingJsRuntime();
        var sut = Sut(js);

        await FluentActions.Awaiting(() => sut.SetReferenceLayerAsync("map1", " ", "[]", new ReferenceLayerOptions(16)))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("layerId");
        await FluentActions.Awaiting(() => sut.SetReferenceLayerAsync("map1", "reference-a", null!, new ReferenceLayerOptions(16)))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("rawFeaturesJson");
        await FluentActions.Awaiting(() => sut.SetReferenceLayerAsync("map1", "reference-a", "[]", null!))
            .Should().ThrowAsync<ArgumentNullException>().WithParameterName("options");
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearReferenceLayerAsync_CallsClearReferenceLayerWithDivIdAndLayerId()
    {
        var js = new RecordingJsRuntime();

        await Sut(js).ClearReferenceLayerAsync("map1", "reference-a");

        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.clearReferenceLayer");
        call.Args.Should().Equal("map1", "reference-a");
    }

    [Fact]
    public async Task ClearReferenceLayerAsync_BlankLayerId_ThrowsAndMakesNoJsCall()
    {
        var js = new RecordingJsRuntime();

        await FluentActions.Awaiting(() => Sut(js).ClearReferenceLayerAsync("map1", ""))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("layerId");
        js.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetViewBoundsAsync_MapsWestSouthEastNorth()
    {
        var js = new RecordingJsRuntime { Result = new[] { -38.6, -13.0, -38.4, -12.8 } };

        var bounds = await Sut(js).GetViewBoundsAsync("map1");

        bounds.Should().Be(new GeoAssets.Core.Rendering.BoundingBox(-38.6, -13.0, -38.4, -12.8));
        var call = js.Calls.Should().ContainSingle().Subject;
        call.Identifier.Should().Be("GeoAssets.getViewBounds");
        call.Args.Should().Equal("map1");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetViewBoundsAsync_NoMapOrAMalformedAnswer_IsNull(bool malformed)
    {
        var js = new RecordingJsRuntime { Result = malformed ? new[] { 1.0, 2.0 } : null };

        (await Sut(js).GetViewBoundsAsync("map1")).Should().BeNull();
    }

    /// <summary>
    /// The JS half can't run here (no browser), so its contract is read from the script: the three functions exist and
    /// are exported, every reference primitive is built non-interactive and ignored by Geoman, labels are text nodes in
    /// their own pane, and the reference block never calls into .NET nor touches the feature registries the click,
    /// context-menu, selection and snap-scope paths read.
    /// </summary>
    [Fact]
    public void GeoassetsJs_ReferenceLayers_AreNonInteractiveAndNeverReachDotNet()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Shared", "wwwroot", "js", "geoassets.js"));
        var start = script.IndexOf("// ─── Reference layers of another organization (XD01-331)", StringComparison.Ordinal);
        var end = script.IndexOf("// ─── Per-feature states: public API (XD01-309)", StringComparison.Ordinal);
        start.Should().BePositive();
        end.Should().BeGreaterThan(start);
        // Code only: the block's comments name the registries it stays out of.
        var timeout = TimeSpan.FromSeconds(5);
        var block = Regex.Replace(
            Regex.Replace(script[start..end], @"/\*.*?\*/", "", RegexOptions.Singleline, timeout),
            @"//[^\n]*", "", RegexOptions.None, timeout);

        foreach (var function in new[] { "setReferenceLayer", "clearReferenceLayer", "getViewBounds" })
        {
            block.Should().Contain($"function {function}(");
            script[end..].Should().Contain($"        {function},", $"'{function}' must be exported from window.GeoAssets");
        }

        block.Should().Contain("interactive: false").And.Contain("pmIgnore:    true").And.Contain("snapIgnore:  true")
            .And.Contain("bubblingMouseEvents: false").And.Contain("pointerEvents = 'none'");
        block.Should().Contain("..._REFERENCE_LAYER_OPTIONS, radius", "points are built with the same non-interactive options");
        block.Should().Contain("content.textContent = label").And.NotContain("innerHTML");
        block.Should().Contain("pane:        _REFERENCE_LABEL_PANE");

        block.Should().NotContain("invokeMethodAsync").And.NotContain("dotNetRef").And.NotContain("_bindEvents")
            .And.NotContain("featureLayers").And.NotContain("layerGroups").And.NotContain("bindPopup")
            .And.NotContain(".on(", "no event handler is attached to a reference layer");

        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Shared", "wwwroot", "css", "geoassets.css"));
        css.Should().Contain(".geoassets-reference-feature").And.Contain(".geoassets-reference-label")
            .And.Contain(".geoassets-reference-pane");
    }

    /// <summary>
    /// A user feature's name is user data (edited, imported, shared), and Leaflet parses a string tooltip as HTML. So
    /// <c>_bindEvents</c> must hand <c>bindTooltip</c> a text node holding the name, never the name itself (XD01-412).
    /// </summary>
    [Fact]
    public void GeoassetsJs_FeatureTooltip_BindsTheNameAsATextNode()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Shared", "wwwroot", "js", "geoassets.js"));
        var start = script.IndexOf("function _bindEvents(", StringComparison.Ordinal);
        start.Should().BePositive();
        var end = script.IndexOf("layer.on('click'", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var timeout = TimeSpan.FromSeconds(5);
        var block = Regex.Replace(script[start..end], @"//[^\n]*", "", RegexOptions.None, timeout);

        block.Should().Contain("const tooltip = document.createElement('span');")
            .And.Contain("tooltip.textContent = name;")
            .And.Contain("layer.bindTooltip(tooltip,")
            .And.NotContain("bindTooltip(name").And.NotContain("innerHTML");
        Regex.Matches(script, @"\.bindTooltip\(", RegexOptions.None, timeout).Should()
            .HaveCount(2, "the feature tooltip and the reference label are the only tooltips; a new one needs the same text-node check");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
