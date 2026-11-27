using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// A read-only, featureless <see cref="IAssetProvider"/> over an offline raster map pack — PNG, JPEG or WebP tiles
/// (XD01-465). It declares <see cref="ProviderRenderCapabilities.RasterTiles"/> and implements
/// <see cref="ILocalRasterTileProvider"/>, so the map draws it as a basemap tile layer whose images are read from the
/// pack on this device (<see cref="PMTilesRasterTileSource"/>: one directory lookup and one positional read per tile,
/// no network), over the pack's zooms and bounds, rather than reading features. Every feature query answers nothing;
/// writes are ignored and no event is raised, like the other read-only providers.
/// <para>
/// Its tiles are not copied into the host's <see cref="ITileCache"/>: the pack is already the on-device store, so a
/// second copy of every image would only double the space it takes and crowd the vector packs' entries out of the
/// bounded cache.
/// </para>
/// </summary>
public sealed class PMTilesRasterPackAssetProvider : IAssetProvider, IProviderCapabilities, ILocalRasterTileProvider, IDisposable
{
    /// <summary>The layer set every tile of a raster pack is asked for (the pack holds one image layer).</summary>
    public static readonly TileLayerSet LayerSet = new("pmtiles", ["pmtiles"]);

    private readonly PMTilesPack _pack;
    private readonly PMTilesRasterTileSource _tiles;

    /// <summary>A provider over <paramref name="pack"/>, which it disposes with itself.</summary>
    /// <exception cref="PMTilesFormatException"><paramref name="pack"/> is not a raster pack.</exception>
    public PMTilesRasterPackAssetProvider(PMTilesPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _pack = pack;
        _tiles = new PMTilesRasterTileSource(pack.Archive);
        RasterTileLayer = LayerOf(pack);
    }

    /// <summary>The pack this provider reads.</summary>
    public PMTilesPack Pack => _pack;

    public ProviderRenderCapabilities RenderCapabilities => ProviderRenderCapabilities.RasterTiles;

    public IRasterTileSource RasterTiles => _tiles;

    public LocalRasterTileLayer RasterTileLayer { get; }

    public event EventHandler<GeoFeature>? FeatureAdded      { add { } remove { } }
    public event EventHandler<GeoFeature>? FeatureUpdated    { add { } remove { } }
    public event EventHandler<string>?     FeatureDeleted    { add { } remove { } }
    public event EventHandler?             CollectionChanged { add { } remove { } }

    public GeoFeature? GetById(string id) => null;
    public IReadOnlyList<GeoFeature> GetAll() => [];
    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => [];
    public IReadOnlyList<GeoFeature> Search(string query) => [];
    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => [];
    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => [];
    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => [];

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Task.FromResult<IReadOnlyList<GeoFeature>>([]);

    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Task.FromResult<IReadOnlyList<JsonElement>>([]);

    public Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Task.FromResult<string?>(null);

    public IReadOnlyList<AssetType> GetAssetTypes() => [.. AssetType.Defaults];
    public IReadOnlyList<Layer> GetLayers() => [];
    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => [];

    public void Add(GeoFeature feature) { }
    public void Update(GeoFeature feature) { }
    public void AddRange(IEnumerable<GeoFeature> features) { }
    public void Delete(string id) { }
    public void Clear() { }
    public void LoadAll(IEnumerable<GeoFeature> features) { }
    public void AddAssetType(AssetType assetType) { }
    public void DeleteAssetType(Guid id) { }
    public void AddLayer(Layer layer) { }
    public void DeleteLayer(Guid id) { }
    public void AddLayerRule(LayerRule layerRule) { }
    public void DeleteLayerRule(Guid id) { }

    public void Dispose()
    {
        _tiles.Dispose();
        _pack.Dispose();
    }

    private static LocalRasterTileLayer LayerOf(PMTilesPack pack)
    {
        var header = pack.Header;
        if (pack.Kind != PMTilesPackKind.Raster)
            throw new PMTilesFormatException($"The pack holds {header.TileType} tiles, not PNG, JPEG or WebP images.");

        var (west, south, east, north) = header.Bounds;
        BoundingBox? bounds = west < east && south < north
            ? new BoundingBox(Math.Max(west, -180), Math.Max(south, -WebMercator.MaxLatitude), Math.Min(east, 180), Math.Min(north, WebMercator.MaxLatitude))
            : null;
        return new LocalRasterTileLayer(
            LayerSet,
            PMTilesRasterImages.ContentType(header.TileType),
            header.MinZoom,
            Math.Min((int)header.MaxZoom, TileAddress.MaxZoom),
            bounds);
    }
}
