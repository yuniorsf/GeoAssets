using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// XD01-429 — the viewport half of <see cref="MapInteropService"/>: the raw-JSON and JSON-element batches carry their
/// features' resolved styles (the raw pan path used to drop them), each JS batch carries only its own features'
/// styles (the whole map used to be re-sent with every batch), and the viewport pass calls reach
/// <c>geoassets.js</c>'s <c>beginViewportRender</c>/<c>endViewportRender</c>.
/// </summary>
public sealed class MapInteropServiceViewportTests
{
    private const string Div = "map1";

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];
        public object? Result { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(Result is TValue value ? value : default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public IEnumerable<object?[]> Batches => Calls.Where(c => string.Equals(c.Identifier, "GeoAssets.renderFeatureBatch", StringComparison.Ordinal)).Select(c => c.Args);
    }

    /// <summary>A repository with a "Pole" type whose features resolve to "#ff0000" by default and to "#00ff00" when
    /// their <c>material</c> is <c>steel</c>.</summary>
    private static (TestAssetProvider Repo, AssetType Pole) StyledRepository()
    {
        var repo = new TestAssetProvider();
        var defaultLayer = new Layer { Name = "Default", Color = "#ff0000" };
        var steelLayer = new Layer { Name = "Steel", Color = "#00ff00" };
        repo.AddLayer(defaultLayer);
        repo.AddLayer(steelLayer);
        var pole = new AssetType { Name = "Pole", DefaultLayerId = defaultLayer.Id };
        repo.AddAssetType(pole);
        repo.AddLayerRule(new LayerRule
        {
            AssetTypeId = pole.Id,
            LayerId = steelLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        });
        return (repo, pole);
    }

    private static MapInteropService Sut(RecordingJsRuntime js, TestAssetProvider repo, MapInteropOptions? options = null) =>
        new(js, repo, Options.Create(options ?? new MapInteropOptions()));

    private static string RawFeatures(AssetType type) =>
        """
        [
          {"type":"Feature","id":"plain","geometry":{"type":"Point","coordinates":[-38.5,-12.9]},"properties":{"assetTypeId":"TYPE"}},
          {"type":"Feature","id":"steel","geometry":{"type":"Point","coordinates":[-38.5,-12.9]},"properties":{"assetTypeId":"TYPE","customAttributes":{"material":"steel"}}},
          {"type":"Feature","id":"unknown","geometry":null,"properties":{"assetTypeId":"OTHER"}}
        ]
        """.Replace("TYPE", type.Id.ToString(), StringComparison.Ordinal).Replace("OTHER", Guid.NewGuid().ToString(), StringComparison.Ordinal);

    private static Dictionary<string, LayerStyleOptions>? StylesOf(object?[] batchArgs) =>
        (Dictionary<string, LayerStyleOptions>?)batchArgs[3];

    [Fact]
    public async Task RenderFeatureBatchRawJsonAsync_SendsTheChunksResolvedStyles()
    {
        var (repo, pole) = StyledRepository();
        var js = new RecordingJsRuntime();
        var raw = RawFeatures(pole);

        await Sut(js, repo).RenderFeatureBatchRawJsonAsync(Div, raw);

        var args = js.Batches.Should().ContainSingle().Subject;
        args[0].Should().Be(Div);
        args[1].Should().BeSameAs(raw, "the raw string still goes to JS as it came");
        var styles = StylesOf(args);
        styles.Should().NotBeNull().And.HaveCount(2);
        styles!["plain"].Color.Should().Be("#ff0000");
        styles["steel"].Color.Should().Be("#00ff00", "a rule on a custom attribute applies on the raw path too");
        styles.Should().NotContainKey("unknown");
    }

    [Fact]
    public async Task RenderFeatureBatchRawJsonAsync_NoLayers_SendsTheJsonUnread()
    {
        var js = new RecordingJsRuntime();

        // Not even JSON: with no layer nothing could resolve, so the chunk is never parsed in .NET.
        await Sut(js, new TestAssetProvider()).RenderFeatureBatchRawJsonAsync(Div, "not json");

        StylesOf(js.Batches.Should().ContainSingle().Subject).Should().BeNull();
    }

    [Fact]
    public async Task RenderAllFeaturesRawJsonAsync_ClearsThenSendsTheResolvedStyles()
    {
        var (repo, pole) = StyledRepository();
        var js = new RecordingJsRuntime();

        await Sut(js, repo).RenderAllFeaturesRawJsonAsync(Div, RawFeatures(pole));

        js.Calls.Select(c => c.Identifier).Should().Equal("GeoAssets.clearAllFeatures", "GeoAssets.renderFeatureBatch");
        StylesOf(js.Batches.Single())!.Keys.Should().BeEquivalentTo("plain", "steel");
    }

    [Fact]
    public async Task RenderFeatureBatchAsync_JsonElements_SendTheirResolvedStyles()
    {
        var (repo, pole) = StyledRepository();
        var js = new RecordingJsRuntime();
        using var doc = JsonDocument.Parse(RawFeatures(pole));
        var elements = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

        await Sut(js, repo, new MapInteropOptions { SinglePass = true }).RenderFeatureBatchAsync(Div, elements);

        var styles = StylesOf(js.Batches.Should().ContainSingle().Subject);
        styles!["steel"].Color.Should().Be("#00ff00");
        styles.Keys.Should().BeEquivalentTo("plain", "steel");
    }

    [Fact]
    public async Task RenderAllFeaturesAsync_JsonElements_ClearsThenSendsTheirResolvedStyles()
    {
        var (repo, pole) = StyledRepository();
        var js = new RecordingJsRuntime();
        using var doc = JsonDocument.Parse(RawFeatures(pole));
        var elements = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

        await Sut(js, repo, new MapInteropOptions { SinglePass = true }).RenderAllFeaturesAsync(Div, elements);

        js.Calls[0].Identifier.Should().Be("GeoAssets.clearAllFeatures");
        StylesOf(js.Batches.Single())!.Keys.Should().BeEquivalentTo("plain", "steel");
    }

    [Fact]
    public async Task RenderFeatureBatchAsync_JsonElements_NoLayers_SendNoStyles()
    {
        var js = new RecordingJsRuntime();
        using var doc = JsonDocument.Parse("""[{"id":"a","properties":{}}]""");

        await Sut(js, new TestAssetProvider()).RenderFeatureBatchAsync(Div, [doc.RootElement[0].Clone()]);

        StylesOf(js.Batches.Should().ContainSingle().Subject).Should().BeNull();
    }

    [Fact]
    public async Task RenderFeatureBatchAsync_EachJsBatchCarriesOnlyItsOwnFeaturesStyles()
    {
        // Before XD01-429 every 2-feature call carried all five features' styles.
        var (repo, pole) = StyledRepository();
        var js = new RecordingJsRuntime();
        var features = Enumerable.Range(0, 5)
            .Select(i => new GeoFeature { Id = $"f{i}", Properties = { AssetTypeId = pole.Id.ToString() } })
            .ToList();

        await Sut(js, repo, new MapInteropOptions { BatchSize = 2 }).RenderFeatureBatchAsync(Div, features);

        js.Batches.Select(args => StylesOf(args)!.Keys.Order(StringComparer.Ordinal).ToArray())
            .Should().BeEquivalentTo(new[] { new[] { "f0", "f1" }, ["f2", "f3"], ["f4"] }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task RenderFeatureBatchAsync_ABatchWithoutStyles_SendsNone()
    {
        var js = new RecordingJsRuntime();

        await Sut(js, new TestAssetProvider(), new MapInteropOptions { SinglePass = true })
            .RenderFeatureBatchAsync(Div, [new GeoFeature { Id = "a" }]);

        StylesOf(js.Batches.Should().ContainSingle().Subject).Should().BeNull();
    }

    [Theory]
    [InlineData(0, 2, new[] { "a" })]
    [InlineData(1, 2, new[] { "c" })]
    [InlineData(1, 1, new string[0])]
    public void StylesFor_PicksTheBatchsOwnStyles(int start, int count, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var style = new LayerStyleOptions("#000000", 1, 1, "#000000", 0.5, null, null);
        var styleMap = new Dictionary<string, LayerStyleOptions>(StringComparer.Ordinal) { ["a"] = style, ["c"] = style };

        var batch = MapInteropService.StylesFor(styleMap, ["a", "b", "c"], start, count);

        if (expected.Length == 0) batch.Should().BeNull();
        else batch!.Keys.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void StylesFor_NoMapOrNoIds_IsNull()
    {
        var style = new LayerStyleOptions("#000000", 1, 1, "#000000", 0.5, null, null);

        MapInteropService.StylesFor(null, ["a"], 0, 1).Should().BeNull();
        MapInteropService.StylesFor(new Dictionary<string, LayerStyleOptions>(StringComparer.Ordinal), ["a"], 0, 1).Should().BeNull();
        MapInteropService.StylesFor(new Dictionary<string, LayerStyleOptions>(StringComparer.Ordinal) { ["a"] = style }, null, 0, 1).Should().BeNull();
    }

    [Fact]
    public async Task ViewportPass_CallsBeginAndEndViewportRender()
    {
        var js = new RecordingJsRuntime { Result = 4 };
        var sut = Sut(js, new TestAssetProvider());

        var generation = await sut.BeginViewportRenderAsync(Div);
        await sut.EndViewportRenderAsync(Div, generation);

        generation.Should().Be(4);
        js.Calls.Should().HaveCount(2);
        js.Calls[0].Identifier.Should().Be("GeoAssets.beginViewportRender");
        js.Calls[0].Args.Should().Equal(Div);
        js.Calls[1].Identifier.Should().Be("GeoAssets.endViewportRender");
        js.Calls[1].Args.Should().Equal(Div, 4);
    }

    /// <summary>
    /// The JS half can't run here (no browser, no JS test runner in this repo), so its contract is read from the
    /// script: moveend/zoomend become one viewport call carrying the zoom, a batch skips a feature drawn with the same
    /// data and style, a superseded batch stops, the pass functions are exported, and a refresh never clears.
    /// </summary>
    [Fact]
    public void GeoassetsJs_ViewportPass_DiffsInsteadOfClearing()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Shared", "wwwroot", "js", "geoassets.js"));
        var timeout = TimeSpan.FromSeconds(5);
        var code = Regex.Replace(Regex.Replace(script, @"/\*.*?\*/", "", RegexOptions.Singleline, timeout), @"//[^\n]*", "", RegexOptions.None, timeout);

        code.Should().Contain("map.on('moveend zoomend', emitViewport);")
            .And.Contain("queueMicrotask(")
            .And.Contain("b.getWest(), b.getSouth(), b.getEast(), b.getNorth(), Math.round(map.getZoom())");
        code.Should().NotContain("map.on('moveend', emitViewport)").And.NotContain("map.on('zoomend', emitViewport)");

        code.Should().Contain("function beginViewportRender(divId)").And.Contain("function endViewportRender(divId, generation)");
        var exports = script[script.LastIndexOf("return {", StringComparison.Ordinal)..];
        exports.Should().Contain("        beginViewportRender,").And.Contain("        endViewportRender,");

        var batch = code[code.IndexOf("function renderFeatureBatch(", StringComparison.Ordinal)..code.IndexOf("function removeFeature(", StringComparison.Ordinal)];
        batch.Should().Contain("state.renderGeneration !== generation")
            .And.Contain("_renderBatchFeature(divId, state,")
            .And.NotContain("clearAllFeatures(");
        code.Should().Contain("state.featureSignatures.get(feature.id) === signature");

        var end = code[code.IndexOf("function endViewportRender(", StringComparison.Ordinal)..];
        end = end[..end.IndexOf("\n    }", StringComparison.Ordinal)];
        end.Should().Contain("pass.generation !== generation").And.Contain("removeFeature(divId, id)")
            .And.NotContain("featureStates").And.NotContain("referenceLayers").And.NotContain("tileLayers");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
