using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// How a map draws the raster tiles an <see cref="Interfaces.ILocalRasterTileProvider"/> reads on this device (XD01-465):
/// the <see cref="IRasterTileSource"/> is asked for <see cref="LayerSet"/> at the XYZ tiles of zooms <see cref="MinZoom"/>
/// to <see cref="MaxZoom"/> (a closer map shows the tiles of <see cref="MaxZoom"/> scaled up), inside
/// <see cref="Bounds"/> when known, and each answer is an image in <see cref="ContentType"/>.
/// </summary>
public sealed record LocalRasterTileLayer
{
    /// <summary>A layer of <paramref name="contentType"/> images of <paramref name="layerSet"/> over zooms <paramref name="minZoom"/>–<paramref name="maxZoom"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is blank or not an <c>image/*</c> type.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="layerSet"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A zoom is outside the tile pyramid, or <paramref name="minZoom"/> is above <paramref name="maxZoom"/>.</exception>
    public LocalRasterTileLayer(TileLayerSet layerSet, string contentType, int minZoom, int maxZoom, BoundingBox? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(layerSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"A raster tile layer serves images, not '{contentType}'.", nameof(contentType));
        ArgumentOutOfRangeException.ThrowIfLessThan(minZoom, TileAddress.MinZoom);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxZoom, TileAddress.MaxZoom);
        if (minZoom > maxZoom)
            throw new ArgumentOutOfRangeException(nameof(minZoom), minZoom, string.Create(CultureInfo.InvariantCulture, $"The lowest zoom is above the highest ({maxZoom})."));

        LayerSet = layerSet;
        ContentType = contentType;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Bounds = bounds;
    }

    /// <summary>What each tile is asked for.</summary>
    public TileLayerSet LayerSet { get; }

    /// <summary>The media type of every tile image, e.g. <see cref="TileFormats.PngContentType"/>.</summary>
    public string ContentType { get; }

    /// <summary>The lowest zoom with tiles; nothing is drawn below it.</summary>
    public int MinZoom { get; }

    /// <summary>The highest zoom with tiles.</summary>
    public int MaxZoom { get; }

    /// <summary>Where the tiles are (SRID 4326); <c>null</c> = anywhere.</summary>
    public BoundingBox? Bounds { get; }
}
