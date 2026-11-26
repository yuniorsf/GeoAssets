using System.Globalization;
using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// Where a committed write changed what tiles show (ADR-002 D6, XD01-431): the longitude/latitude
/// <see cref="Bounds"/> of the change — a feature's envelope before or after it (a moved feature dirties both, as two
/// regions) — at zooms <see cref="MinZoom"/>…<see cref="MaxZoom"/>. It names no organization or layer: every scope's and
/// layer set's tiles there are dirty, since a shared or granted feature shows in other organizations' tiles too.
/// Always valid (the constructor checks it); <c>default</c> is the point (0, 0) at zoom 0.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct DirtyTileRegion
{
    /// <summary>The change inside <paramref name="bounds"/>, at zooms <paramref name="minZoom"/>…<paramref name="maxZoom"/> (every zoom by default).</summary>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not finite, or a minimum is above its maximum.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A zoom is outside the pyramid, or <paramref name="minZoom"/> is above <paramref name="maxZoom"/>.</exception>
    public DirtyTileRegion(BoundingBox bounds, int minZoom = TileAddress.MinZoom, int maxZoom = TileAddress.MaxZoom)
    {
        if (!double.IsFinite(bounds.MinLon) || !double.IsFinite(bounds.MinLat) || !double.IsFinite(bounds.MaxLon) || !double.IsFinite(bounds.MaxLat))
            throw new ArgumentException("The box's coordinates must be finite numbers.", nameof(bounds));
        if (bounds.MinLon > bounds.MaxLon || bounds.MinLat > bounds.MaxLat)
            throw new ArgumentException("The box's minimum must not be above its maximum.", nameof(bounds));
        TileAddress.EnsureZoom(minZoom, nameof(minZoom));
        TileAddress.EnsureZoom(maxZoom, nameof(maxZoom));
        if (minZoom > maxZoom)
            throw new ArgumentOutOfRangeException(nameof(minZoom), minZoom, string.Create(CultureInfo.InvariantCulture, $"The minimum zoom must not be above the maximum ({maxZoom})."));

        Bounds = bounds;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
    }

    /// <summary>The changed area (SRID 4326), edges inclusive.</summary>
    public BoundingBox Bounds { get; }

    /// <summary>The lowest dirty zoom.</summary>
    public int MinZoom { get; }

    /// <summary>The highest dirty zoom.</summary>
    public int MaxZoom { get; }

    /// <summary>
    /// The dirty tiles, one <see cref="TileRange"/> per zoom from <see cref="MinZoom"/> up to <see cref="MaxZoom"/>:
    /// <see cref="TileRange.Covering"/> <see cref="Bounds"/> grown by <paramref name="buffer"/> tiles (the tile source's
    /// clip/symbol buffer, so a change near a tile's edge also dirties the neighbor that draws it). A range's
    /// <see cref="TileRange.Count"/> grows fourfold per zoom — bound it (e.g. by the zooms actually cached) before
    /// walking every tile.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="buffer"/> is negative or not finite.</exception>
    public IEnumerable<TileRange> TileRanges(double buffer = 0)
    {
        if (!double.IsFinite(buffer) || buffer < 0)
            throw new ArgumentOutOfRangeException(nameof(buffer), buffer, "The tile buffer must be a finite, non-negative number of tiles.");

        return Ranges(Bounds, MinZoom, MaxZoom, buffer);
    }

    private static IEnumerable<TileRange> Ranges(BoundingBox bounds, int minZoom, int maxZoom, double buffer)
    {
        for (var zoom = minZoom; zoom <= maxZoom; zoom++)
            yield return TileRange.Covering(bounds, zoom, buffer);
    }
}
