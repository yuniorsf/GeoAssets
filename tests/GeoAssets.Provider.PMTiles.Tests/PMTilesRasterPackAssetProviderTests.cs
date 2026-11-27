using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-465 — <see cref="PMTilesRasterPackAssetProvider"/>: a featureless, read-only provider declaring raster tiles, whose
/// map layer — the pack's images, zooms and bounds — is what the map's local raster renderer draws.
/// </summary>
public sealed class PMTilesRasterPackAssetProviderTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private Task<PMTilesPack> RasterPackAsync(byte minZoom = 0, byte maxZoom = 14, (double, double, double, double)? bounds = null) =>
        PMTilesPack.OpenAsync(
            TestRasterTiles.Archive(PMTilesTileType.Png, [new TileAddress(0, 0, 0)], minZoom, maxZoom, bounds).WriteTo(_directory.Path),
            CancellationToken.None);

    [Fact]
    public async Task ItDeclaresRasterTilesOnly_AndIsALocalRasterTileProvider()
    {
        using var provider = new PMTilesRasterPackAssetProvider(await RasterPackAsync());

        provider.GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.RasterTiles);
        ((IAssetProvider)provider).Should().BeAssignableTo<ILocalRasterTileProvider>();
        provider.RasterTileLayer.LayerSet.Should().BeSameAs(PMTilesRasterPackAssetProvider.LayerSet);
    }

    [Fact]
    public async Task ItsLayer_IsThePacksZoomsAndBounds_ClampedToWhatAMapShows()
    {
        using var provider = new PMTilesRasterPackAssetProvider(await RasterPackAsync(3, 30, (-200, -89, 200, 89)));

        provider.RasterTileLayer.MinZoom.Should().Be(3);
        provider.RasterTileLayer.MaxZoom.Should().Be(TileAddress.MaxZoom);
        provider.RasterTileLayer.Bounds.Should().Be(new BoundingBox(-180, -WebMercator.MaxLatitude, 180, WebMercator.MaxLatitude));
    }

    [Fact]
    public async Task APackWithoutBounds_HasALayerOfUnknownBounds()
    {
        using var provider = new PMTilesRasterPackAssetProvider(await RasterPackAsync(bounds: (0, 0, 0, 0)));

        provider.RasterTileLayer.Bounds.Should().BeNull();
    }

    [Fact]
    public async Task EveryFeatureQuery_AnswersNothing_AndWritesAreIgnored()
    {
        using var provider = new PMTilesRasterPackAssetProvider(await RasterPackAsync());
        var feature = new GeoFeature { Id = "f", Geometry = new GeoPoint(0, 0) };
        var raised = 0;
        provider.FeatureAdded += (_, _) => raised++;
        provider.FeatureUpdated += (_, _) => raised++;
        provider.FeatureDeleted += (_, _) => raised++;
        provider.CollectionChanged += (_, _) => raised++;

        provider.Add(feature);
        provider.AddRange([feature]);
        provider.Update(feature);
        provider.LoadAll([feature]);
        provider.Delete("f");
        provider.Clear();
        provider.AddAssetType(AssetType.Defaults.First());
        provider.DeleteAssetType(Guid.NewGuid());
        provider.AddLayer(new Layer());
        provider.DeleteLayer(Guid.NewGuid());
        provider.AddLayerRule(new LayerRule());
        provider.DeleteLayerRule(Guid.NewGuid());

        raised.Should().Be(0);
        provider.GetAll().Should().BeEmpty();
        provider.GetById("f").Should().BeNull();
        provider.GetByAssetType("Point").Should().BeEmpty();
        provider.Search("f").Should().BeEmpty();
        provider.GetWithin(new GeoPoint(0, 0)).Should().BeEmpty();
        provider.GetIntersecting(new GeoPoint(0, 0)).Should().BeEmpty();
        provider.GetNearby(new GeoPoint(0, 0), 1).Should().BeEmpty();
        (await provider.GetInBoundsAsync(-1, -1, 1, 1)).Should().BeEmpty();
        (await provider.GetInBoundsJsonAsync(-1, -1, 1, 1)).Should().BeEmpty();
        (await provider.GetInBoundsRawJsonAsync(-1, -1, 1, 1)).Should().BeNull();
        provider.GetAssetTypes().Should().HaveCount(AssetType.Defaults.Count());
        provider.GetLayers().Should().BeEmpty();
        provider.GetLayerRules(Guid.NewGuid()).Should().BeEmpty();
    }

    [Fact]
    public async Task AVectorPack_IsRefused()
    {
        using var pack = await PMTilesPack.OpenAsync(TestPacks.Write(_directory.Path, [new TileAddress(0, 0, 0)]), CancellationToken.None);

        var act = () => new PMTilesRasterPackAssetProvider(pack);

        act.Should().Throw<PMTilesFormatException>();
    }

    [Fact]
    public async Task Disposing_ClosesThePack()
    {
        var pack = await RasterPackAsync();
        var provider = new PMTilesRasterPackAssetProvider(pack);

        provider.Dispose();

        var act = () => pack.Archive.GetTileAsync(new TileAddress(0, 0, 0), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
        FluentActions.Invoking(() => new PMTilesRasterPackAssetProvider(null!)).Should().Throw<ArgumentNullException>();
    }
}
