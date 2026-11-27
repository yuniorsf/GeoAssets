using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Mvt;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-438 — <see cref="PMTilesPackAssetProvider"/>: an offline pack drawn through the map's vector path, one bounded
/// viewport read at a time, at the tile zoom MapLibre would read, read-only.
/// </summary>
public sealed class PMTilesPackAssetProviderTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private async Task<PMTilesPackAssetProvider> ProviderAsync(IEnumerable<TileAddress> tiles, byte maxZoom = 14, byte minZoom = 0,
        (double, double, double, double)? bounds = null, ITileCache? cache = null)
    {
        var pack = await PMTilesPack.OpenAsync(TestPacks.Write(_directory.Path, tiles, maxZoom, minZoom, bounds), CancellationToken.None);
        return new PMTilesPackAssetProvider(pack, cache);
    }

    private static IEnumerable<TileAddress> AllTiles(int zoom) => new TileRange(zoom, 0, 0, (1 << zoom) - 1, (1 << zoom) - 1).EnumerateTiles();

    [Fact]
    public async Task ItDrawsAsVectorFeaturesOnly()
    {
        using var provider = await ProviderAsync([new TileAddress(0, 0, 0)]);

        provider.GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.VectorFeatures);
    }

    [Fact]
    public async Task TheOverview_IsTheWholePackAtItsHighestZoomOfAtMostSixteenTiles()
    {
        using var provider = await ProviderAsync([new TileAddress(0, 0, 0), .. AllTiles(2), .. AllTiles(3)], maxZoom: 3);

        var overview = await provider.LoadOverviewAsync();

        overview.Should().HaveCount(16);
        overview.Select(f => f.Properties.Name).Should().OnlyContain(name => name.StartsWith("2/", StringComparison.Ordinal));
        provider.GetAll().Should().BeEquivalentTo(overview);
    }

    [Fact]
    public async Task TheOverview_OfAPackWhoseLowestZoomIsWide_IsTheTileUnderItsCenter()
    {
        var center = TileAddress.Containing(1, -2, 6);
        using var provider = await ProviderAsync([center, new TileAddress(6, 0, 0)], maxZoom: 8, minZoom: 6);

        var overview = await provider.LoadOverviewAsync();

        overview.Should().ContainSingle().Which.Properties.Name.Should().Be(center.ToString());
    }

    [Fact]
    public async Task AViewport_IsReadAtTheTileZoomOneBelowTheMapZoom()
    {
        using var provider = await ProviderAsync([.. AllTiles(3), .. AllTiles(4)], maxZoom: 4);
        var bounds = new TileAddress(4, 9, 6).Envelope4326;

        var features = await provider.ReadAsync(Shrunk(bounds), mapZoom: 4);

        features.Should().ContainSingle().Which.Properties.Name.Should().Be("3/4/3");
    }

    [Fact]
    public async Task AMapZoomedInPastThePack_IsReadAtThePacksHighestZoom()
    {
        using var provider = await ProviderAsync(AllTiles(2), maxZoom: 2);
        var bounds = new TileAddress(10, 300, 400).Envelope4326;

        var features = await provider.ReadAsync(bounds, mapZoom: 12);

        features.Should().ContainSingle().Which.Properties.Name.Should().Be("2/1/1");
    }

    [Fact]
    public async Task AMapZoomedOutPastThePack_ReadsNothing()
    {
        using var provider = await ProviderAsync([new TileAddress(5, 10, 10)], maxZoom: 8, minZoom: 5);

        provider.TilesFor(new BoundingBox(-10, -10, 10, 10), mapZoom: 5).Should().BeNull();
        (await provider.ReadAsync(new BoundingBox(-10, -10, 10, 10), mapZoom: 5)).Should().BeEmpty();
    }

    [Fact]
    public async Task AViewOutsideThePacksBounds_ReadsNothing()
    {
        using var provider = await ProviderAsync([new TileAddress(0, 0, 0)], bounds: (-9.3, 38.6, -9.0, 38.8));

        provider.TilesFor(new BoundingBox(10, 10, 20, 20), mapZoom: 10).Should().BeNull();
    }

    [Fact]
    public async Task OnlyThePartOfTheViewInsideThePack_IsRead()
    {
        using var provider = await ProviderAsync([new TileAddress(0, 0, 0)], maxZoom: 14, bounds: (-9.3, 38.6, -9.0, 38.8));

        var range = provider.TilesFor(new BoundingBox(-30, 20, 30, 60), mapZoom: 13)!.Value;

        range.Zoom.Should().Be(12);
        range.Should().Be(TileRange.Covering(new BoundingBox(-9.3, 38.6, -9.0, 38.8), 12));
    }

    [Fact]
    public async Task AWideView_StepsDownAZoom_UntilItsTilesFitTheReadLimit()
    {
        using var provider = await ProviderAsync([new TileAddress(0, 0, 0)]);

        var range = provider.TilesFor(new BoundingBox(-180, -85, 180, 85), mapZoom: 15)!.Value;

        range.Zoom.Should().Be(3);
        range.Count.Should().BeLessThanOrEqualTo(PMTilesPackAssetProvider.MaxTilesPerRead);
    }

    [Fact]
    public async Task ARead_AnswersAtMostTheFeatureLimit()
    {
        var points = Enumerable.Range(0, PMTilesPackAssetProvider.MaxFeaturesPerRead + 50)
            .Select(i => TestMvt.PointAt(i % 4096, i / 4096))
            .ToArray();
        var archive = new PMTilesTestArchive { MaxZoom = 0 }
            .Add(new TileAddress(0, 0, 0), PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer("dense", points))));
        using var pack = await PMTilesPack.OpenAsync(archive.WriteTo(_directory.Path), CancellationToken.None);
        using var provider = new PMTilesPackAssetProvider(pack);

        var features = await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 1);

        features.Should().HaveCount(PMTilesPackAssetProvider.MaxFeaturesPerRead);
    }

    [Fact]
    public async Task FeatureIds_NameThePackTheTileTheLayerAndTheFeature_AndAreTheSameOnEveryRead()
    {
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1);
        var bounds = new BoundingBox(1, 1, 2, 2);

        var first = await provider.ReadAsync(bounds, mapZoom: 2);
        var second = await provider.ReadAsync(bounds, mapZoom: 2);

        first.Single().Id.Should().Be($"pmtiles:{provider.Pack.Id}:1/1/0:0:0");
        second.Select(f => f.Id).Should().Equal(first.Select(f => f.Id));
    }

    [Fact]
    public async Task TheViewportJsonRead_IsTheViewportsFeaturesAsGeoJson()
    {
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1);
        IAssetProvider asProvider = provider;

        var json = await asProvider.GetInBoundsJsonAsync(new ViewportQuery(new BoundingBox(1, 1, 2, 2), Zoom: 2));

        var feature = json.Should().ContainSingle().Subject;
        feature.GetProperty("type").GetString().Should().Be("Feature");
        feature.GetProperty("id").GetString().Should().EndWith(":1/1/0:0:0");
        feature.GetProperty("geometry").GetProperty("type").GetString().Should().Be("Point");
        feature.GetProperty("properties").GetProperty("customAttributes").GetProperty(MvtFeatureProjector.LayerAttribute).GetString().Should().Be("pois");
    }

    [Fact]
    public async Task TheBoundsReads_WithoutAZoom_ReadThePacksHighestFittingZoom()
    {
        using var provider = await ProviderAsync([.. AllTiles(1), .. AllTiles(2)], maxZoom: 2);

        var features = await provider.GetInBoundsAsync(1, 1, 2, 2);
        var json = await provider.GetInBoundsJsonAsync(1, 1, 2, 2);

        features.Single().Properties.Name.Should().Be("2/2/1");
        json.Should().ContainSingle().Which.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task WhatWasReadLast_AnswersTheCatalogQueries()
    {
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1);
        var features = await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);
        var first = features[0];

        provider.GetAll().Should().HaveCount(4);
        provider.GetById(first.Id).Should().BeSameAs(first);
        provider.GetById("missing").Should().BeNull();
        provider.Search("1/0/1").Should().ContainSingle().Which.Properties.Name.Should().Be("1/0/1");
        provider.Search("pois").Should().HaveCount(4, "a feature's attributes (its layer) are searched too");
        provider.Search(" ").Should().HaveCount(4);
        provider.GetByAssetType(AssetType.Point.Id.ToString()).Should().HaveCount(4);
        provider.GetByAssetType(AssetType.Line.Id.ToString()).Should().BeEmpty();

        var northEast = new GeoPolygon([(0.0, 0.0), (180.0, 0.0), (180.0, 85.0), (0.0, 85.0), (0.0, 0.0)]);
        provider.GetWithin(northEast).Should().ContainSingle().Which.Properties.Name.Should().Be("1/1/0");
        provider.GetIntersecting(northEast).Should().ContainSingle();
        provider.GetNearby(new GeoPoint(90, 66.5), 1).Should().ContainSingle().Which.Properties.Name.Should().Be("1/1/0");
    }

    [Fact]
    public async Task ACancelledRead_Throws_AndKeepsWhatWasReadBefore()
    {
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1);
        await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => provider.ReadAsync(new BoundingBox(1, 1, 2, 2), mapZoom: 2, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.GetAll().Should().HaveCount(4);
    }

    [Fact]
    public async Task Reads_GoThroughTheTileCache()
    {
        var cache = new RecordingTileCache();
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1, cache: cache);

        await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);
        await provider.ReadAsync(new BoundingBox(-180, -85, 180, 85), mapZoom: 2);

        cache.Sets.Should().HaveCount(4, "each tile is read from the pack once");
        cache.Gets.Should().HaveCount(8);
    }

    [Fact]
    public async Task Writes_AreIgnored_AndTheTypesAreTheDefaults()
    {
        using var provider = await ProviderAsync(AllTiles(1), maxZoom: 1);
        await provider.LoadOverviewAsync();
        var before = provider.GetAll();

        provider.Add(new GeoFeature());
        provider.Update(before[0]);
        provider.AddRange([new GeoFeature()]);
        provider.Delete(before[0].Id);
        provider.Clear();
        provider.LoadAll([]);
        provider.AddAssetType(new AssetType());
        provider.DeleteAssetType(AssetType.Point.Id);
        provider.AddLayer(new Layer());
        provider.DeleteLayer(Guid.NewGuid());
        provider.AddLayerRule(new LayerRule());
        provider.DeleteLayerRule(Guid.NewGuid());

        provider.GetAll().Should().BeEquivalentTo(before);
        provider.GetAssetTypes().Select(t => t.Id).Should().Equal(AssetType.Defaults.Select(t => t.Id));
        provider.GetLayers().Should().BeEmpty();
        provider.GetLayerRules(AssetType.Point.Id).Should().BeEmpty();
    }

    /// <summary>A box a hair inside <paramref name="bounds"/>, so it is covered by that tile alone.</summary>
    private static BoundingBox Shrunk(BoundingBox bounds)
    {
        var lon = (bounds.MaxLon - bounds.MinLon) * 0.01;
        var lat = (bounds.MaxLat - bounds.MinLat) * 0.01;
        return new BoundingBox(bounds.MinLon + lon, bounds.MinLat + lat, bounds.MaxLon - lon, bounds.MaxLat - lat);
    }
}
