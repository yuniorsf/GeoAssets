using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Provider.WMS;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Rendering;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>
/// XD01-436 — <see cref="HybridLodMapLayerRenderer"/>: it takes a provider whose server serves the map's tiles (and
/// declines everything else), draws it from the tiles — in MapLibre mode only the selected and just-written features as
/// a GeoJSON overlay read by id, their tile copies hidden until a re-read of the style after the write reports another
/// <c>{v}</c> — and, in the interim Leaflet split, the raster tiles at the raster tier's zooms and the features as before
/// at every other zoom and without a raster tier.
/// </summary>
public sealed class HybridLodMapLayerRendererTests
{
    private const string Div = "geoassets-map";
    private static readonly Uri Gateway = new("https://gw.example/");
    private static readonly BoundingBox Bounds = new(-38.6, -13.05, -38.4, -12.8);

    private readonly LayerRecordingMapInterop _map = new();
    private readonly FakeTileLayerInterop _tiles;
    private readonly FeatureSelectionState _selection = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

    public HybridLodMapLayerRendererTests() => _tiles = new FakeTileLayerInterop(_map);

    private HybridLodMapLayerRenderer Renderer(
        MapRenderMode mode = MapRenderMode.MapLibre, bool split = false, bool withTiles = true, bool withGateway = true,
        string? styleUrl = null) =>
        new(_map, new FeatureRenderPipeline(),
            Options.Create(new MapInteropOptions
            {
                RenderMode = mode,
                LeafletTileSplit = split,
                MapLibre = new MapLibreOptions { StyleUrl = styleUrl ?? MapLibreOptions.DefaultStyleUrl }
            }),
            withTiles ? _tiles : null, _selection,
            withGateway ? new ApiScopeRoutes(Gateway, "api://kernel/access_as_user") : null,
            _time);

    private static GeoFeature Pole(string id) =>
        new() { Id = id, Geometry = new GeoPoint(-38.5, -12.9), Properties = new GeoFeatureProperties { Name = id } };

    private readonly Dictionary<IAssetProvider, ProviderEntry> _entries = [];

    /// <summary>The request of the provider's one pool entry, as the map makes it for every call.</summary>
    private MapLayerRenderRequest Request(IAssetProvider provider)
    {
        if (!_entries.TryGetValue(provider, out var entry))
            _entries[provider] = entry = new ProviderEntry { Name = "Kernel", Provider = provider };
        return MapLayerRenderRequest.For(Div, entry);
    }

    private static ViewportQuery At(int zoom) => new(Bounds, zoom);

    // ── Which providers ───────────────────────────────────────────────────────

    [Fact]
    public void Takes_a_kernel_provider_in_MapLibre_mode_also_through_the_decorator_chain()
    {
        var renderer = Renderer();

        renderer.CanRender(new KernelLayerProvider()).Should().BeTrue();
        renderer.CanRender(new ValidatingAssetProvider(new KernelLayerProvider())).Should().BeTrue();
    }

    [Fact]
    public void Declines_a_provider_without_HybridLod_another_server_a_WMS_and_one_without_an_address()
    {
        var renderer = Renderer();

        renderer.CanRender(new KernelLayerProvider(AssetProviderExtensions.DefaultVectorCapabilities)).Should().BeFalse();
        renderer.CanRender(new FakeLayerProvider()).Should().BeFalse("an undeclared provider is plain vector features");
        renderer.CanRender(new KernelLayerProvider(baseAddress: "https://elsewhere.example/api/geoassets/")).Should().BeFalse();
        renderer.CanRender(new KernelLayerProvider(baseAddress: "https://gw.example:8443/api/geoassets/")).Should().BeFalse("another port is another server");
        renderer.CanRender(new KernelLayerProvider(baseAddress: null)).Should().BeFalse();
        renderer.CanRender(new WmsAssetProvider("https://gw.example/wms", "geoassets:feature")).Should().BeFalse();
    }

    [Fact]
    public void Declines_in_a_Leaflet_mode_unless_the_split_is_on()
    {
        Renderer(MapRenderMode.Canvas).CanRender(new KernelLayerProvider()).Should().BeFalse();
        Renderer(MapRenderMode.Leaflet).CanRender(new KernelLayerProvider()).Should().BeFalse();
        Renderer(MapRenderMode.Canvas, split: true).CanRender(new KernelLayerProvider()).Should().BeTrue();
    }

    [Fact]
    public void Declines_without_a_tile_interop_or_with_a_style_it_cannot_place()
    {
        Renderer(withTiles: false).CanRender(new KernelLayerProvider()).Should().BeFalse();
        Renderer(withGateway: false).CanRender(new KernelLayerProvider()).Should().BeFalse("a relative style URL names no server");
        Renderer(withGateway: false, styleUrl: "https://gw.example/tiles/style.json").CanRender(new KernelLayerProvider()).Should().BeTrue();
    }

    [Fact]
    public void IsServedBy_compares_scheme_host_and_port()
    {
        var style = new Uri("https://GW.example/tiles/style.json");
        HybridLodMapLayerRenderer.IsServedBy(new Uri("https://gw.example/api/geoassets/"), style).Should().BeTrue();
        HybridLodMapLayerRenderer.IsServedBy(new Uri("http://gw.example/api/geoassets/"), style).Should().BeFalse();
        HybridLodMapLayerRenderer.IsServedBy(new Uri("/api/geoassets/", UriKind.Relative), style).Should().BeFalse();
    }

    [Fact]
    public void Wins_over_the_vector_and_WMS_renderers_whatever_their_registration_order()
    {
        var hybrid = Renderer();
        var vector = new VectorFeatureMapLayerRenderer(_map, new FeatureRenderPipeline());
        var wms = new WmsMapLayerRenderer(_map);

        foreach (IProviderMapLayerRenderer[] order in new[]
                 {
                     new IProviderMapLayerRenderer[] { wms, vector, hybrid },
                     [vector, hybrid, wms],
                     [hybrid, wms, vector]
                 })
        {
            var chain = new ProviderMapLayerRendererChain(order);
            chain.Renderers[0].Should().BeSameAs(hybrid);
            chain.Resolve(new KernelLayerProvider()).Should().BeSameAs(hybrid);
            chain.Resolve(new KernelLayerProvider(baseAddress: "https://elsewhere.example/")).Should().BeSameAs(vector);
            chain.Resolve(new FakeLayerProvider()).Should().BeSameAs(vector);
            chain.Resolve(new FakeWmsLayerProvider()).Should().BeSameAs(wms);
        }
        ((IProviderMapLayerRenderer)hybrid).Precedence.Should().BeGreaterThan(((IProviderMapLayerRenderer)vector).Precedence);
        ((IProviderMapLayerRenderer)vector).Precedence.Should().Be(MapLayerRendererPrecedence.Default);
        ((IProviderMapLayerRenderer)wms).Precedence.Should().Be(MapLayerRendererPrecedence.Default);
    }

    // ── MapLibre: tiles are the display ──────────────────────────────────────

    [Fact]
    public async Task Render_shows_the_tiles_and_never_enumerates_or_fetches_the_catalog()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"), Pole("b"));
        using var renderer = Renderer();

        var drawn = await renderer.RenderAsync(Request(kernel));
        var pass = await renderer.RenderViewportAsync(Request(kernel), At(16));

        drawn.Should().Be(0);
        pass.Should().Be(0);
        kernel.GetAllCalls.Should().Be(0);
        kernel.BoundsReads.Should().Be(0);
        _map.FeatureBatches.Should().BeEmpty();
        _map.RawJsonBatches.Should().BeEmpty();
        _tiles.Shows.Should().Be(1, "a viewport pass after the render reuses what the style answered");
        renderer.ClearedWithFeatures.Should().BeFalse("the tile layer survives ClearAllFeatures");
    }

    [Fact]
    public async Task Selecting_a_kernel_feature_draws_its_overlay_by_id_then_hides_its_tile_copy()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"), Pole("b"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _map.Calls.Clear();

        _selection.Select(kernel.GetById("a")!);

        _map.Calls.Should().Equal("render:a", "hidden:[a]");
        kernel.ByIdReads.Should().Contain("a");
        kernel.GetAllCalls.Should().Be(0);
        _tiles.Hidden[Div].Should().Equal("a");
    }

    [Fact]
    public async Task Deselecting_shows_the_tile_copy_again_before_the_overlay_goes()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _selection.Select(kernel.GetById("a")!);
        _map.Calls.Clear();

        _selection.Clear();

        _map.Calls.Should().Equal("hidden:[]", "remove:a");
    }

    [Fact]
    public async Task A_new_feature_being_drawn_or_one_of_another_provider_draws_no_overlay()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _map.Calls.Clear();

        _selection.Select(Pole("drawn"), isNew: true);
        _selection.Select(Pole("not-in-the-kernel"));

        _map.RenderedFeatures.Should().BeEmpty();
        _map.Calls.Should().NotContain(c => c.StartsWith("render:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_viewport_pass_draws_the_overlay_again_so_the_pass_keeps_it()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _selection.Select(kernel.GetById("a")!);
        _map.Calls.Clear();

        var drawn = await renderer.RenderViewportAsync(Request(kernel), At(17));

        drawn.Should().Be(1);
        _map.Calls.Should().StartWith("render:a");
        kernel.BoundsReads.Should().Be(0);
    }

    [Fact]
    public async Task A_written_feature_stays_an_overlay_over_a_hidden_tile_copy_until_a_later_re_read_reports_another_v()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _map.Calls.Clear();
        var beforeTheWrite = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMilliseconds(10));

        kernel.Update(Pole("a"));
        _map.Calls.Should().Equal("render:a", "hidden:[a]");

        // A re-read that started before the write, even on another {v}, can't carry it.
        _tiles.Refreshed(Div, "abc-e1-0.2", beforeTheWrite);
        // A re-read after the write that still sees the same {v}: the server didn't commit it yet.
        _time.Advance(TimeSpan.FromSeconds(1.5));
        _tiles.Refreshed(Div, "abc-e1-0.2", _time.GetUtcNow());
        _map.Calls.Should().Equal("render:a", "hidden:[a]");

        // The write was made while the map drew 0.1; a re-read started after it now sees 0.3.
        _time.Advance(TimeSpan.FromSeconds(1));
        _tiles.Refreshed(Div, "abc-e1-0.3", _time.GetUtcNow());

        _map.Calls.Should().Equal("render:a", "hidden:[a]", "hidden:[]", "remove:a");
    }

    [Fact]
    public async Task A_written_feature_that_is_still_selected_keeps_its_overlay_after_its_v()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _selection.Select(kernel.GetById("a")!);
        kernel.Update(Pole("a"));
        _map.Calls.Clear();

        _time.Advance(TimeSpan.FromSeconds(2));
        _tiles.Refreshed(Div, "abc-e1-0.2", _time.GetUtcNow());

        _map.Calls.Should().BeEmpty("the selection still wants the overlay and its hidden tile copy");
        _tiles.Hidden[Div].Should().Equal("a");
    }

    [Fact]
    public async Task A_deleted_feature_has_its_tile_copy_hidden_until_its_v()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        kernel.Update(Pole("a"));
        _map.Calls.Clear();

        kernel.Delete("a");
        _time.Advance(TimeSpan.FromSeconds(2));
        _tiles.Refreshed(Div, "abc-e1-0.2", _time.GetUtcNow());

        _map.Calls.Should().Equal("remove:a", "hidden:[]");
    }

    [Fact]
    public async Task Hiding_the_entry_takes_the_tiles_and_the_overlay_off_and_stops_following_its_writes()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));
        _selection.Select(kernel.GetById("a")!);
        kernel.HasWriteSubscribers.Should().BeTrue();
        _map.Calls.Clear();

        await renderer.RemoveAsync(Request(kernel));

        _map.Calls.Should().Equal("hide-tiles", "remove:a");
        kernel.HasWriteSubscribers.Should().BeFalse();
        kernel.GetAllCalls.Should().Be(0);
    }

    [Fact]
    public async Task Removing_an_entry_it_never_drew_only_hides_the_tiles()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer();

        await renderer.RemoveAsync(Request(kernel));

        _map.Calls.Should().Equal("hide-tiles");
        kernel.GetAllCalls.Should().Be(0, "a hidden entry is removed at every bulk render: that must stay cheap");
    }

    [Fact]
    public async Task While_the_style_cannot_be_read_it_draws_as_the_vector_renderer_does()
    {
        _tiles.Info = null;
        var kernel = new KernelLayerProvider { RawJson = """[{"id":"a"}]""" }.With(Pole("a"));
        using var renderer = Renderer();

        var drawn = await renderer.RenderAsync(Request(kernel));
        await renderer.RenderViewportAsync(Request(kernel), At(16));
        _selection.Select(kernel.GetById("a")!);

        drawn.Should().Be(1);
        kernel.GetAllCalls.Should().Be(1);
        kernel.BoundsReads.Should().Be(1);
        _map.RenderedFeatures.Should().BeEmpty("the vector path draws the feature already");
        _tiles.Hidden.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_unsubscribes_from_the_selection_the_tiles_and_the_providers()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        var renderer = Renderer();
        await renderer.RenderAsync(Request(kernel));

        renderer.Dispose();
        _selection.Select(kernel.GetById("a")!);

        kernel.HasWriteSubscribers.Should().BeFalse();
        _tiles.HasSubscribers.Should().BeFalse();
        _map.RenderedFeatures.Should().BeEmpty();
    }

    // ── The interim Leaflet split ─────────────────────────────────────────────

    [Theory]
    [InlineData(5, false)]
    [InlineData(13, false)]
    [InlineData(14, true)]
    [InlineData(18, true)]
    public async Task Leaflet_split_fetches_features_only_outside_the_raster_tier(int zoom, bool fetches)
    {
        _tiles.Info = new MapTileStyleInfo("abc-e1-0.1", 0, 13, 15);
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer(MapRenderMode.Canvas, split: true);
        await renderer.RenderAsync(Request(kernel));
        var readsBefore = kernel.BoundsReads;

        await renderer.RenderViewportAsync(Request(kernel), At(zoom));

        (kernel.BoundsReads > readsBefore).Should().Be(fetches);
        _tiles.Hidden.Should().BeEmpty("a raster tile can't hide one feature");
    }

    [Fact]
    public async Task Leaflet_split_renders_the_whole_layer_when_the_map_opens_above_the_raster_tier_and_only_the_overlay_inside_it()
    {
        var kernel = new KernelLayerProvider().With(Pole("a"));
        _tiles.Info = new MapTileStyleInfo("abc-e1-0.1", 0, 13, 15);
        using (var above = Renderer(MapRenderMode.Leaflet, split: true))
            await above.RenderAsync(Request(kernel));
        kernel.GetAllCalls.Should().Be(1);

        _tiles.Info = new MapTileStyleInfo("abc-e1-0.1", 0, 13, 9);
        using var inside = Renderer(MapRenderMode.Leaflet, split: true);
        _selection.Select(kernel.GetById("a")!);
        _map.Calls.Clear();
        await inside.RenderAsync(Request(kernel));

        kernel.GetAllCalls.Should().Be(1);
        _map.Calls.Should().Equal("show-tiles", "render:a");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(18)]
    public async Task Leaflet_split_without_a_raster_tier_draws_the_features_at_every_zoom_as_before(int zoom)
    {
        _tiles.Info = new MapTileStyleInfo("abc-e1-0.1", null, null, zoom);
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer(MapRenderMode.WebGL, split: true);

        await renderer.RenderAsync(Request(kernel));
        await renderer.RenderViewportAsync(Request(kernel), At(zoom));
        _selection.Select(kernel.GetById("a")!);

        kernel.GetAllCalls.Should().Be(1);
        kernel.BoundsReads.Should().Be(1);
        _map.RenderedFeatures.Should().BeEmpty();
        _tiles.Hidden.Should().BeEmpty();
    }

    [Fact]
    public async Task Leaflet_split_re_reads_the_raster_tier_when_v_moves_and_releases_the_overlay_inside_it()
    {
        _tiles.Info = new MapTileStyleInfo("abc-e1-0.1", 0, 13, 9);
        var kernel = new KernelLayerProvider().With(Pole("a"));
        using var renderer = Renderer(MapRenderMode.Leaflet, split: true);
        await renderer.RenderAsync(Request(kernel));
        kernel.Update(Pole("a"));
        _map.Calls.Clear();

        _time.Advance(TimeSpan.FromSeconds(2));
        _tiles.Refreshed(Div, "abc-e1-0.2", _time.GetUtcNow());

        _tiles.Shows.Should().Be(2);
        _map.Calls.Should().Equal("show-tiles", "remove:a");
    }

    [Fact]
    public void MapTileStyleInfo_IsRasterZoom_is_the_raster_tiers_inclusive_run()
    {
        var info = new MapTileStyleInfo("v", 4, 13, 10);
        info.HasRasterTier.Should().BeTrue();
        info.IsRasterZoom(3).Should().BeFalse();
        info.IsRasterZoom(4).Should().BeTrue();
        info.IsRasterZoom(13).Should().BeTrue();
        info.IsRasterZoom(14).Should().BeFalse();
        new MapTileStyleInfo("v", null, null, 10).IsRasterZoom(10).Should().BeFalse();
        new MapTileStyleInfo("v", null, null, 10).HasRasterTier.Should().BeFalse();
    }
}
