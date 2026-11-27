using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authentication;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// XD01-435 — <see cref="MapInteropService"/> in <see cref="MapRenderMode.MapLibre"/>: every call goes to
/// <c>GeoAssetsMapLibre.*</c> once <c>geoassets.maplibre.js</c> is imported (and only then, and only in that mode), the
/// map is initialized with the style under the gateway, and the provider's writes and the clock re-read the tile style.
/// </summary>
public sealed class MapInteropServiceMapLibreTests
{
    private const string Div = "map1";
    private static readonly Uri Gateway = new("https://gw.example/");

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];
        public int FailImports { get; set; }
        public object? Result { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            if (string.Equals(identifier, "import", StringComparison.Ordinal) && FailImports > 0)
            {
                FailImports--;
                return ValueTask.FromException<TValue>(new JSException("Failed to fetch dynamically imported module"));
            }
            return ValueTask.FromResult(Result is TValue value ? value : default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public IEnumerable<string> Identifiers => Calls.Select(c => c.Identifier);
    }

    private static MapInteropService Sut(
        RecordingJsRuntime js, TestAssetProvider? repo = null, MapRenderMode mode = MapRenderMode.MapLibre,
        FakeTimeProvider? time = null, ApiScopeRoutes? routes = null, MapLibreOptions? mapLibre = null) =>
        new(js, repo ?? new TestAssetProvider(),
            Options.Create(new MapInteropOptions { RenderMode = mode, MapLibre = mapLibre ?? new MapLibreOptions() }),
            time ?? new FakeTimeProvider(), routes);

    [Fact]
    public async Task MapLibre_mode_imports_the_module_once_before_its_first_call_and_calls_its_namespace()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js);

        await sut.SetLayerVisibilityAsync(Div, "type-a", false);
        await sut.ClearAllFeaturesAsync(Div);
        await sut.SetFeatureStatesAsync(Div, ["a"], MapFeatureState.Selected);

        js.Identifiers.Should().Equal(
            "import",
            "GeoAssetsMapLibre.setLayerVisibility",
            "GeoAssetsMapLibre.clearAllFeatures",
            "GeoAssetsMapLibre.setFeatureStates");
        js.Calls[0].Args.Should().Equal(MapInteropService.MapLibreModuleUrl);
        js.Calls[1].Args.Should().Equal(Div, "type-a", false);
    }

    [Fact]
    public async Task MapLibre_mode_returns_the_modules_answers()
    {
        var js = new RecordingJsRuntime { Result = new[] { -39.0, -13.0, -38.0, -12.0 } };
        await using var sut = Sut(js);

        var bounds = await sut.GetViewBoundsAsync(Div);

        bounds.Should().Be(new BoundingBox(-39.0, -13.0, -38.0, -12.0));
        js.Identifiers.Should().Equal("import", "GeoAssetsMapLibre.getViewBounds");
    }

    [Theory]
    [InlineData(MapRenderMode.Leaflet)]
    [InlineData(MapRenderMode.Canvas)]
    [InlineData(MapRenderMode.WebGL)]
    public async Task The_Leaflet_modes_never_import_the_MapLibre_module(MapRenderMode mode)
    {
        var js = new RecordingJsRuntime();
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, repo, mode, time);

        await sut.InitializeMapAsync(Div, -12.9, -38.5, 15);
        await sut.EnableDrawModeAsync(Div, GeometryType.Point);
        repo.Add(new GeoFeature { Id = "f1" });
        time.Advance(TimeSpan.FromMinutes(5));

        js.Identifiers.Should().Equal("GeoAssets.initializeMap", "GeoAssets.enableDraw");
        js.Calls[0].Args.Should().Equal(Div, -12.9, -38.5, 15, mode.ToString().ToUpperInvariant());
        sut.MapLibreMap.Should().BeNull();
    }

    [Fact]
    public async Task InitializeMap_passes_the_style_under_the_gateway()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js, routes: new ApiScopeRoutes(Gateway, "api://kernel/access_as_user"));

        await sut.InitializeMapAsync(Div, -12.9, -38.5, 15);

        js.Identifiers.Should().Equal("import", "GeoAssetsMapLibre.initializeMap");
        js.Calls[1].Args.Should().Equal(Div, -12.9, -38.5, 15, "MAPLIBRE",
            new MapLibreMapOptions("https://gw.example/tiles/style.json", "https://gw.example/"));
    }

    [Fact]
    public async Task Without_a_gateway_the_style_is_relative_to_the_page()
    {
        var js = new RecordingJsRuntime();
        await using var sut = Sut(js, mapLibre: new MapLibreOptions { StyleUrl = "tiles/style.json?layers=assets" });

        await sut.InitializeMapAsync(Div, 0, 0, 3);

        js.Calls[1].Args[5].Should().Be(new MapLibreMapOptions("tiles/style.json?layers=assets", null));
    }

    [Fact]
    public async Task A_failed_import_is_retried_by_the_next_call()
    {
        var js = new RecordingJsRuntime { FailImports = 1 };
        await using var sut = Sut(js);

        var first = () => sut.InvalidateSizeAsync(Div);
        await first.Should().ThrowAsync<JSException>();
        await sut.InvalidateSizeAsync(Div);

        js.Identifiers.Should().Equal("import", "import", "GeoAssetsMapLibre.invalidateSize");
    }

    [Fact]
    public async Task A_write_re_reads_the_tile_style_of_an_initialized_map_after_the_write_delay()
    {
        var js = new RecordingJsRuntime();
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, repo, time: time,
            mapLibre: new MapLibreOptions { StyleRefreshInterval = TimeSpan.Zero, WriteRefreshDelay = TimeSpan.FromSeconds(2) });

        repo.Add(new GeoFeature { Id = "before-the-map" });
        time.Advance(TimeSpan.FromSeconds(2));
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        repo.Add(new GeoFeature { Id = "f1" });
        repo.Update(new GeoFeature { Id = "f1" });
        time.Advance(TimeSpan.FromSeconds(1));
        repo.Delete("f1");
        time.Advance(TimeSpan.FromSeconds(2));

        js.Calls.Where(c => string.Equals(c.Identifier, "GeoAssetsMapLibre.refreshTileStyle", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Args.Should().Equal(Div);
    }

    [Fact]
    public async Task A_collection_change_re_reads_the_tile_style_too()
    {
        var js = new RecordingJsRuntime();
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, repo, time: time, mapLibre: new MapLibreOptions { StyleRefreshInterval = TimeSpan.Zero });
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        repo.Clear();
        time.Advance(TimeSpan.FromSeconds(1.5));

        js.Identifiers.Should().Contain("GeoAssetsMapLibre.refreshTileStyle");
    }

    [Fact]
    public async Task The_tile_style_is_re_read_every_interval_until_the_map_is_destroyed()
    {
        var js = new RecordingJsRuntime();
        var time = new FakeTimeProvider();
        await using var sut = Sut(js, time: time, mapLibre: new MapLibreOptions { StyleRefreshInterval = TimeSpan.FromSeconds(30) });
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        time.Advance(TimeSpan.FromSeconds(30));
        time.Advance(TimeSpan.FromSeconds(30));
        await sut.DestroyMapAsync(Div);
        time.Advance(TimeSpan.FromMinutes(5));

        js.Identifiers.Where(i => string.Equals(i, "GeoAssetsMapLibre.refreshTileStyle", StringComparison.Ordinal)).Should().HaveCount(2);
        js.Identifiers.Last().Should().Be("GeoAssetsMapLibre.destroyMap");
    }

    [Fact]
    public async Task Writes_after_dispose_re_read_nothing()
    {
        var js = new RecordingJsRuntime();
        var repo = new TestAssetProvider();
        var time = new FakeTimeProvider();
        var sut = Sut(js, repo, time: time);
        await sut.InitializeMapAsync(Div, 0, 0, 3);

        await sut.DisposeAsync();
        repo.Add(new GeoFeature { Id = "f1" });
        time.Advance(TimeSpan.FromMinutes(5));

        js.Identifiers.Should().NotContain("GeoAssetsMapLibre.refreshTileStyle");
    }
}
