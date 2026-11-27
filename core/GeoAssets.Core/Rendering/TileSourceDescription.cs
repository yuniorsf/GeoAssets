using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// What an <see cref="IDescribedTileSource"/> holds (XD01-459). Always valid: the constructor refuses a version or layer id
/// that is not a <see cref="TileKeySegment"/>, no layer or a repeated one, a zoom range outside the pyramid or reversed,
/// and bounds that are not finite longitude/latitude with the west below the east and the south below the north.
/// </summary>
public sealed record TileSourceDescription
{
    /// <param name="version">
    /// The version of what the source serves — the <c>{v}</c> of its tile URLs: it changes whenever its tiles may (e.g. a
    /// hash of its file's length and modification time), so a URL under it can be cached as immutable.
    /// </param>
    /// <param name="layerIds">The layers to request (<see cref="TileLayerSet.LayerIds"/>) for all of it: a vector source's MVT layers, a raster's own id.</param>
    /// <param name="minZoom">The lowest zoom it has tiles at.</param>
    /// <param name="maxZoom">The highest zoom it has tiles at (a client overzooms past it).</param>
    /// <param name="bounds">The longitude/latitude extent it covers; <c>null</c> = unknown.</param>
    public TileSourceDescription(string version, IEnumerable<string> layerIds, int minZoom, int maxZoom, BoundingBox? bounds = null)
    {
        TileKeySegment.EnsureValid(version, nameof(version));
        ArgumentNullException.ThrowIfNull(layerIds);

        var layers = layerIds.ToArray();
        if (layers.Length == 0)
            throw new ArgumentException("A tile source must name at least one layer.", nameof(layerIds));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            TileKeySegment.EnsureValid(layer, nameof(layerIds));
            if (!seen.Add(layer))
                throw new ArgumentException($"The layer '{layer}' appears more than once.", nameof(layerIds));
        }

        if (minZoom < TileAddress.MinZoom || minZoom > TileAddress.MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(minZoom), minZoom, ZoomMessage);
        if (maxZoom < minZoom || maxZoom > TileAddress.MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(maxZoom), maxZoom, ZoomMessage);

        if (bounds is { } b && !(IsLongitude(b.MinLon) && IsLongitude(b.MaxLon) && IsLatitude(b.MinLat) && IsLatitude(b.MaxLat)
                                 && b.MinLon < b.MaxLon && b.MinLat < b.MaxLat))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "The bounds must be finite longitudes/latitudes, west below east and south below north.");
        }

        Version = version;
        LayerIds = layers;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Bounds = bounds;
    }

    /// <summary>The version of what the source serves: its tile URLs' <c>{v}</c>.</summary>
    public string Version { get; }

    /// <summary>The layers to request for all of it.</summary>
    public IReadOnlyList<string> LayerIds { get; }

    /// <summary>The lowest zoom it has tiles at.</summary>
    public int MinZoom { get; }

    /// <summary>The highest zoom it has tiles at.</summary>
    public int MaxZoom { get; }

    /// <summary>The longitude/latitude extent it covers; <c>null</c> = unknown.</summary>
    public BoundingBox? Bounds { get; }

    private static string ZoomMessage =>
        string.Create(CultureInfo.InvariantCulture, $"The zoom range must be within [{TileAddress.MinZoom}, {TileAddress.MaxZoom}], lowest first.");

    private static bool IsLongitude(double value) => double.IsFinite(value) && value is >= -180 and <= 180;

    private static bool IsLatitude(double value) => double.IsFinite(value) && value is >= -90 and <= 90;
}
