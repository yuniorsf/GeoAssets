using System.Globalization;
using System.Runtime.InteropServices;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// A rectangle of tiles at one zoom, bounds inclusive (XD01-431): columns <see cref="MinX"/>…<see cref="MaxX"/> and
/// rows <see cref="MinY"/>…<see cref="MaxY"/> of zoom <see cref="Zoom"/> (XYZ). Always valid — the constructor
/// refuses a bound outside the zoom's grid or a minimum above its maximum. What a metatile, a tile's descendants and
/// a dirty region at one zoom are (<see cref="TileAddress.Metatile"/>, <see cref="TileAddress.Descendants"/>,
/// <see cref="DirtyTileRegion.TileRanges"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileRange
{
    /// <summary>The tiles of zoom <paramref name="zoom"/> from (<paramref name="minX"/>, <paramref name="minY"/>) to (<paramref name="maxX"/>, <paramref name="maxY"/>), inclusive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The zoom or a bound is outside the pyramid, or a minimum is above its maximum.</exception>
    public TileRange(int zoom, int minX, int minY, int maxX, int maxY)
    {
        var n = TileAddress.TilesPerAxis(zoom);
        EnsureAxis(minX, maxX, n, nameof(minX), nameof(maxX));
        EnsureAxis(minY, maxY, n, nameof(minY), nameof(maxY));

        Zoom = zoom;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>The zoom level of every tile in the range.</summary>
    public int Zoom { get; }

    /// <summary>The westmost column.</summary>
    public int MinX { get; }

    /// <summary>The northmost row (XYZ).</summary>
    public int MinY { get; }

    /// <summary>The eastmost column.</summary>
    public int MaxX { get; }

    /// <summary>The southmost row (XYZ).</summary>
    public int MaxY { get; }

    /// <summary>The number of columns.</summary>
    public int Width => MaxX - MinX + 1;

    /// <summary>The number of rows.</summary>
    public int Height => MaxY - MinY + 1;

    /// <summary>The number of tiles — up to <c>4^24</c> for the whole world at the highest zoom, hence a <see cref="long"/>.</summary>
    public long Count => (long)Width * Height;

    /// <summary>The range holding <paramref name="address"/> alone.</summary>
    public static TileRange Of(TileAddress address) => new(address.Z, address.X, address.Y, address.X, address.Y);

    /// <summary>
    /// The tiles of <paramref name="zoom"/> a change inside <paramref name="bounds"/> shows in: those whose extent
    /// meets the box grown by <paramref name="buffer"/> tiles on every side (a vector tile's clip buffer, a raster
    /// renderer's symbol buffer — e.g. <c>64.0 / 4096</c> for an MVT extent of 4096 with a buffer of 64), edges
    /// inclusive. The box is clamped to the world first, so a box touching ±180° or beyond ±
    /// <see cref="WebMercator.MaxLatitude"/> (the poles) ends at the first/last column/row; a degenerate box (a point)
    /// is the tile containing it, plus its buffer.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="bounds"/> is not finite, or a minimum is above its maximum (a box across the antimeridian is two boxes).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside the pyramid, or <paramref name="buffer"/> is negative or not finite.</exception>
    public static TileRange Covering(BoundingBox bounds, int zoom, double buffer = 0)
    {
        if (!double.IsFinite(buffer) || buffer < 0)
            throw new ArgumentOutOfRangeException(nameof(buffer), buffer, "The tile buffer must be a finite, non-negative number of tiles.");
        if (bounds.MinLon > bounds.MaxLon || bounds.MinLat > bounds.MaxLat)
            throw new ArgumentException("The box's minimum must not be above its maximum.", nameof(bounds));

        var n = TileAddress.TilesPerAxis(zoom);
        return new TileRange(
            zoom,
            TileAddress.ClampToGrid(WebMercator.TileFractionX(bounds.MinLon, n) - buffer, n),
            TileAddress.ClampToGrid(WebMercator.TileFractionY(bounds.MaxLat, n) - buffer, n),
            TileAddress.ClampToGrid(WebMercator.TileFractionX(bounds.MaxLon, n) + buffer, n),
            TileAddress.ClampToGrid(WebMercator.TileFractionY(bounds.MinLat, n) + buffer, n));
    }

    /// <summary>Whether <paramref name="address"/> is one of the range's tiles.</summary>
    public bool Contains(TileAddress address) =>
        address.Z == Zoom && address.X >= MinX && address.X <= MaxX && address.Y >= MinY && address.Y <= MaxY;

    /// <summary>Every tile of the range, row by row from the north-west corner. Lazy: mind <see cref="Count"/> before walking a large range.</summary>
    public IEnumerable<TileAddress> EnumerateTiles() => Enumerate(Zoom, MinX, MinY, MaxX, MaxY);

    /// <summary>The range as <c>z/minX-maxX/minY-maxY</c>, in invariant digits.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Zoom}/{MinX}-{MaxX}/{MinY}-{MaxY}");

    private static IEnumerable<TileAddress> Enumerate(int zoom, int minX, int minY, int maxX, int maxY)
    {
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                yield return new TileAddress(zoom, x, y);
    }

    private static void EnsureAxis(int min, int max, int tilesPerAxis, string minName, string maxName)
    {
        if ((uint)min >= (uint)tilesPerAxis)
            throw new ArgumentOutOfRangeException(minName, min, string.Create(CultureInfo.InvariantCulture, $"The bound must be in [0, {tilesPerAxis})."));
        if ((uint)max >= (uint)tilesPerAxis)
            throw new ArgumentOutOfRangeException(maxName, max, string.Create(CultureInfo.InvariantCulture, $"The bound must be in [0, {tilesPerAxis})."));
        if (min > max)
            throw new ArgumentOutOfRangeException(minName, min, "The minimum must not be above the maximum.");
    }
}
