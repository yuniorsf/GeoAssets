using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// XD01-436 — <see cref="MapInteropService"/> as <see cref="IMapTileLayerInterop"/>: the tile layer calls reach the render
/// mode's namespace with the style under the gateway, a Leaflet map follows <c>{v}</c> only while its tile layer is shown,
/// and every re-read raises <see cref="IMapTileLayerInterop.TileStyleRefreshed"/> with the <c>{v}</c> it answered and
/// when it started.
/// </summary>
public sealed class MapInteropServiceTileLayerTests
{
    private const string Div = "map1";
    private const string StyleUrl = "https://gw.example/tiles/style.json";

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];
        public Dictionary<string, object?> Results { get; } = new(StringComparer.Ordinal);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult(Results.TryGetValue(identifier, out var r) && r is TValue value ? value : default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public IEnumerable<string> Identifiers => Calls.Select(c => c.Identifier);
    }

    private static MapInteropService Sut(RecordingJsRuntime js, TestAssetProvider repo, MapRenderMode mode, FakeTimeProvider time) =>
        new(js, repo,
            Options.Create(new MapInteropOptions
            {
                RenderMode = mode,
                LeafletTileSplit = true,
                MapLibre = new MapLibreOptions { StyleRefreshInterval = TimeSpan.Zero, WriteRefreshDelay = TimeSpan.FromSeconds(1) }
            }),
            time, new ApiScopeRoutes(new Uri("https://gw.example/"), "api://kernel/access_as_user"));

    [Fact]
    public async Task MapLibre_shows_hides_and_hides_tile_copies_through_its_namespace()
    {
        var info = new MapTileStyleInfo("abc-e1-0.1", null, null, 15);
        var js = new RecordingJsRuntime();
        js.Results["GeoAssetsMapLibre.showTileLayers"] = info;
        await using var sut = Sut(js, new TestAssetProvider(), MapRenderMode.MapLibre, new FakeTimeProvider());

        var shown = await sut.ShowTileLayersAsync(Div);
        await sut.SetHiddenTileFeaturesAsync(Div, ["a", "b"]);
        await sut.HideTileLayersAsync(Div);

        shown.Should().Be(info);
        sut.TileStyleUrl.Should().Be(StyleUrl);
        js.Identifiers.Should().Equal("import", "GeoAssetsMapLibre.showTileLayers", "GeoAssetsMapLibre.setHiddenTileFeatures", "GeoAssetsMapLibre.hideTileLayers");
        js.Calls[1].Args.Should().Equal(Div, StyleUrl);
        js.Calls[2].Args[1].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public async Task MapLibre_raises_the_v_of_each_re_read_with_when_it_started()
    {
        var js = new RecordingJsRuntime();
        js.Results["GeoAssetsMapLibre.refreshTileStyle"] = "abc-e1-0.2";
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, repo, MapRenderMode.MapLibre, time);
        var raised = new List<MapTileStyleRefreshedEventArgs>();
        sut.TileStyleRefreshed += (_, e) => raised.Add(e);
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        repo.Add(new GeoFeature { Id = "f1" });
        time.Advance(TimeSpan.FromSeconds(1));

        raised.Should().ContainSingle();
        raised[0].DivId.Should().Be(Div);
        raised[0].Version.Should().Be("abc-e1-0.2");
        raised[0].StartedAt.Should().Be(time.GetUtcNow());
    }

    [Fact]
    public async Task Leaflet_follows_v_only_while_its_tile_layer_is_shown()
    {
        var js = new RecordingJsRuntime();
        js.Results["GeoAssets.refreshTileStyle"] = "abc-e1-0.2";
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, repo, MapRenderMode.Canvas, time);
        var raised = new List<MapTileStyleRefreshedEventArgs>();
        sut.TileStyleRefreshed += (_, e) => raised.Add(e);
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        repo.Add(new GeoFeature { Id = "before-the-tiles" });
        time.Advance(TimeSpan.FromSeconds(1));
        js.Identifiers.Should().NotContain("GeoAssets.refreshTileStyle", "a Leaflet map without its tile layer re-reads nothing");

        await sut.ShowTileLayersAsync(Div);
        await sut.SetHiddenTileFeaturesAsync(Div, ["a"]);
        repo.Add(new GeoFeature { Id = "f1" });
        time.Advance(TimeSpan.FromSeconds(1));

        await sut.HideTileLayersAsync(Div);
        repo.Add(new GeoFeature { Id = "f2" });
        time.Advance(TimeSpan.FromSeconds(1));

        js.Identifiers.Should().Equal(
            "GeoAssets.initializeMap", "GeoAssets.showTileLayers", "GeoAssets.refreshTileStyle", "GeoAssets.hideTileLayers");
        js.Calls[1].Args.Should().Equal(Div, StyleUrl);
        raised.Should().ContainSingle().Which.Version.Should().Be("abc-e1-0.2");
        js.Identifiers.Should().NotContain(i => i.StartsWith("GeoAssetsMapLibre", StringComparison.Ordinal));
    }
}
