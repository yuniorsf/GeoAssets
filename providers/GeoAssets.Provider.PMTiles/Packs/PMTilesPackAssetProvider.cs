using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PMTiles.Mvt;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// A read-only <see cref="IAssetProvider"/> over an offline map pack (XD01-438), drawn by the map's existing vector path
/// (<see cref="ProviderRenderCapabilities.VectorFeatures"/> — Leaflet can't draw vector tiles, and MAUI stays on
/// Leaflet): each viewport read decodes the pack's tiles under the view into <see cref="GeoFeature"/>s
/// (<see cref="MvtFeatureProjector"/>), at the tile zoom MapLibre would read for it — one below the map's 256 px zoom,
/// within the pack's zoom range. Every read is bounded: at most <see cref="MaxTilesPerRead"/> tiles (a wide view steps
/// down a zoom until it fits; a view below the pack's lowest zoom reads none) and <see cref="MaxFeaturesPerRead"/>
/// features, and only the part of the view inside the pack's bounds is read. Tiles come through
/// <see cref="PMTilesPackTiles"/>, i.e. the host's tile cache.
/// <para>
/// The provider holds what it read last — its <see cref="LoadOverviewAsync">overview</see> of the whole pack when
/// connected, then the latest viewport — and answers <see cref="GetAll"/>, <see cref="GetById"/>, search and the
/// spatial queries over that; it never enumerates the pack. Feature ids are <c>pmtiles:{pack id}:{z}/{x}/{y}:{layer}:{n}</c>
/// (the n-th feature of the tile's layer), the same on every read of that tile. Writes are ignored and no event is
/// raised, like the other read-only providers.
/// </para>
/// </summary>
public sealed class PMTilesPackAssetProvider : IAssetProvider, IProviderCapabilities, IDisposable
{
    /// <summary>The most tiles one read decodes.</summary>
    public const int MaxTilesPerRead = 64;

    /// <summary>The most features one read answers.</summary>
    public const int MaxFeaturesPerRead = 10_000;

    /// <summary>The most tiles the overview read when connecting decodes.</summary>
    public const int OverviewMaxTiles = 16;

    private readonly PMTilesPack _pack;
    private readonly PMTilesPackTiles _tiles;
    private Snapshot _current = Snapshot.Empty;

    /// <summary>A provider over <paramref name="pack"/>, reading its tiles through <paramref name="cache"/> when given.</summary>
    public PMTilesPackAssetProvider(PMTilesPack pack, ITileCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _pack = pack;
        _tiles = new PMTilesPackTiles(pack, cache);
    }

    /// <summary>The pack this provider reads.</summary>
    public PMTilesPack Pack => _pack;

    public ProviderRenderCapabilities RenderCapabilities => ProviderRenderCapabilities.VectorFeatures;

    private Snapshot Current => Volatile.Read(ref _current);

    public event EventHandler<GeoFeature>? FeatureAdded      { add { } remove { } }
    public event EventHandler<GeoFeature>? FeatureUpdated    { add { } remove { } }
    public event EventHandler<string>?     FeatureDeleted    { add { } remove { } }
    public event EventHandler?             CollectionChanged { add { } remove { } }

    /// <summary>
    /// Reads the pack's overview — the whole pack at the highest zoom whose tiles over its bounds number at most
    /// <see cref="OverviewMaxTiles"/> (else the tile under its center at its lowest zoom) — so a newly connected pack has
    /// features to draw and fit the map to.
    /// </summary>
    public Task<IReadOnlyList<GeoFeature>> LoadOverviewAsync(CancellationToken ct = default)
    {
        var header = _pack.Header;
        var bounds = PackBounds();
        for (var z = Math.Min((int)header.MaxZoom, TileAddress.MaxZoom); z >= header.MinZoom; z--)
        {
            var range = TileRange.Covering(bounds, z);
            if (range.Count <= OverviewMaxTiles)
                return ReadTilesAsync(range.EnumerateTiles(), ct);
        }

        var center = TileAddress.Containing(header.Center.Longitude, header.Center.Latitude, header.MinZoom);
        return ReadTilesAsync([center], ct);
    }

    /// <summary>
    /// The pack's features under <paramref name="bounds"/>, decoded from the tiles at the zoom MapLibre reads for a map
    /// at <paramref name="mapZoom"/> (Leaflet's 256 px scale), or — without one — at the pack's highest zoom whose tiles
    /// fit <see cref="MaxTilesPerRead"/>. Bounded as the type describes.
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> ReadAsync(BoundingBox bounds, int? mapZoom, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (TilesFor(bounds, mapZoom) is not { } range)
            return Remember([], ct);

        return await ReadTilesAsync(range.EnumerateTiles(), ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        ReadAsync(new BoundingBox(minLon, minLat, maxLon, maxLat), mapZoom: null);

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        ToJson(await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false));

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(ViewportQuery viewport, CancellationToken ct = default) =>
        ToJson(await ReadAsync(viewport.Bounds, viewport.Zoom, ct).ConfigureAwait(false));

    public GeoFeature? GetById(string id) => Current.ById.GetValueOrDefault(id);

    public IReadOnlyList<GeoFeature> GetAll() => Current.Features;

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        [.. Current.Features.Where(f => string.Equals(f.Properties.AssetTypeId, assetTypeId, StringComparison.Ordinal))];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetAll();

        return [.. Current.Features.Where(f =>
            f.Properties.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || f.Properties.CustomAttributes.Any(kv => kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))];
    }

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return [.. Current.Features.Where(f => f.Geometry is { } g && g.Within(bounds))];
    }

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return [.. Current.Features.Where(f => f.Geometry is { } g && g.Intersects(geometry))];
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees)
    {
        ArgumentNullException.ThrowIfNull(center);
        return [.. Current.Features
            .Where(f => f.Geometry is not null)
            .Select(f => (Feature: f, Distance: f.Geometry!.Distance(center)))
            .Where(x => x.Distance <= distanceDegrees)
            .OrderBy(x => x.Distance)
            .Select(x => x.Feature)];
    }

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

    public void Dispose() => _pack.Dispose();

    /// <summary>
    /// The tiles a read of <paramref name="bounds"/> decodes, or <see langword="null"/> for none: the part of the view
    /// inside the pack's bounds, at the tile zoom for <paramref name="mapZoom"/> (or the pack's highest), stepped down
    /// while more than <see cref="MaxTilesPerRead"/> tiles cover it.
    /// </summary>
    internal TileRange? TilesFor(BoundingBox bounds, int? mapZoom)
    {
        var header = _pack.Header;
        var pack = PackBounds();
        var view = new BoundingBox(
            Math.Max(bounds.MinLon, pack.MinLon),
            Math.Max(bounds.MinLat, pack.MinLat),
            Math.Min(bounds.MaxLon, pack.MaxLon),
            Math.Min(bounds.MaxLat, pack.MaxLat));
        if (!(view.MinLon <= view.MaxLon && view.MinLat <= view.MaxLat))
            return null;

        var maxZoom = Math.Min((int)header.MaxZoom, TileAddress.MaxZoom);
        var zoom = mapZoom is { } z ? z - 1 : maxZoom;
        if (zoom < header.MinZoom)
            return null;

        for (zoom = Math.Min(zoom, maxZoom); zoom >= header.MinZoom; zoom--)
        {
            var range = TileRange.Covering(view, zoom);
            if (range.Count <= MaxTilesPerRead)
                return range;
        }

        return null;
    }

    private BoundingBox PackBounds()
    {
        var (west, south, east, north) = _pack.Header.Bounds;
        return west < east && south < north
            ? new BoundingBox(Math.Max(west, -180), Math.Max(south, -WebMercator.MaxLatitude), Math.Min(east, 180), Math.Min(north, WebMercator.MaxLatitude))
            : new BoundingBox(-180, -WebMercator.MaxLatitude, 180, WebMercator.MaxLatitude);
    }

    private async Task<IReadOnlyList<GeoFeature>> ReadTilesAsync(IEnumerable<TileAddress> tiles, CancellationToken ct)
    {
        var features = new List<GeoFeature>();
        foreach (var tile in tiles)
        {
            ct.ThrowIfCancellationRequested();
            if (features.Count >= MaxFeaturesPerRead)
                break;

            var bytes = await _tiles.GetTileAsync(tile, ct).ConfigureAwait(false);
            if (bytes is null)
                continue;

            var layers = MvtTileDecoder.Decode(bytes, MaxFeaturesPerRead - features.Count);
            for (var l = 0; l < layers.Count; l++)
            {
                for (var n = 0; n < layers[l].Features.Count; n++)
                {
                    var id = $"pmtiles:{_pack.Id}:{tile}:{l}:{n}";
                    if (MvtFeatureProjector.ToGeoFeature(layers[l].Features[n], layers[l], tile, id) is { } feature)
                        features.Add(feature);
                }
            }
        }

        return Remember(features, ct);
    }

    /// <summary>Keeps <paramref name="features"/> as what the provider holds, unless the read was superseded meanwhile.</summary>
    private List<GeoFeature> Remember(List<GeoFeature> features, CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
            Volatile.Write(ref _current, new Snapshot(features));
        return features;
    }

    private static List<JsonElement> ToJson(IReadOnlyList<GeoFeature> features)
    {
        var options = GeoJsonSerializer.CompactOptions;
        return [.. features.Select(f => JsonSerializer.SerializeToElement(f, options))];
    }

    private sealed class Snapshot
    {
        public static readonly Snapshot Empty = new([]);

        public Snapshot(List<GeoFeature> features)
        {
            Features = features;
            ById = features.ToDictionary(f => f.Id, StringComparer.Ordinal);
        }

        public IReadOnlyList<GeoFeature> Features { get; }

        public Dictionary<string, GeoFeature> ById { get; }
    }
}
