using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// How a map draws the vector tiles an <see cref="Interfaces.ILocalVectorTileProvider"/> reads on this device (XD01-460):
/// the <see cref="IVectorTileSource"/> is asked for <see cref="LayerSet"/> at the XYZ tiles of zooms <see cref="MinZoom"/>
/// to <see cref="MaxZoom"/> (a closer map draws the tiles of <see cref="MaxZoom"/> overzoomed), inside
/// <see cref="Bounds"/> when known. <see cref="SourceLayers"/> names the MVT layers known up front; a map may find others
/// in the tiles themselves.
/// </summary>
public sealed record LocalVectorTileLayer
{
    /// <summary>A layer of <paramref name="layerSet"/>'s tiles over zooms <paramref name="minZoom"/>–<paramref name="maxZoom"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="layerSet"/> or <paramref name="sourceLayers"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A zoom is outside the tile pyramid, or <paramref name="minZoom"/> is above <paramref name="maxZoom"/>.</exception>
    public LocalVectorTileLayer(TileLayerSet layerSet, IEnumerable<string> sourceLayers, int minZoom, int maxZoom, BoundingBox? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(layerSet);
        ArgumentNullException.ThrowIfNull(sourceLayers);
        ArgumentOutOfRangeException.ThrowIfLessThan(minZoom, TileAddress.MinZoom);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxZoom, TileAddress.MaxZoom);
        if (minZoom > maxZoom)
            throw new ArgumentOutOfRangeException(nameof(minZoom), minZoom, string.Create(CultureInfo.InvariantCulture, $"The lowest zoom is above the highest ({maxZoom})."));

        LayerSet = layerSet;
        SourceLayers = [.. sourceLayers.Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal)];
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Bounds = bounds;
    }

    /// <summary>What each tile is asked for.</summary>
    public TileLayerSet LayerSet { get; }

    /// <summary>The MVT layer names known up front (e.g. a PMTiles archive's <c>vector_layers</c>), each once; may be empty.</summary>
    public IReadOnlyList<string> SourceLayers { get; }

    /// <summary>The lowest zoom with tiles; nothing is drawn below it.</summary>
    public int MinZoom { get; }

    /// <summary>The highest zoom with tiles.</summary>
    public int MaxZoom { get; }

    /// <summary>Where the tiles are (SRID 4326); <c>null</c> = anywhere.</summary>
    public BoundingBox? Bounds { get; }
}
