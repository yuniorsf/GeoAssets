using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles;

/// <summary>
/// PMTiles v3 tile ids (XD01-437): a tile's id is the number of tiles at every lower zoom (<c>(4^z − 1) / 3</c>) plus
/// its position along the zoom's Hilbert curve, so the tiles of one zoom are numbered in an order that keeps
/// neighbours close (the spec's <c>zxyToTileId</c>/<c>tileIdToZxy</c>).
/// </summary>
public static class PMTilesTileId
{
    /// <summary>The id of tile <paramref name="z"/>/<paramref name="x"/>/<paramref name="y"/>.</summary>
    public static ulong FromTile(TileAddress tile)
    {
        var z = tile.Z;
        var n = 1L << z;
        long x = tile.X, y = tile.Y;
        ulong d = 0;
        for (var s = n / 2; s > 0; s /= 2)
        {
            var rx = (x & s) > 0 ? 1L : 0L;
            var ry = (y & s) > 0 ? 1L : 0L;
            d += (ulong)(s * s * ((3 * rx) ^ ry));
            Rotate(s, ref x, ref y, rx, ry);
        }

        return ZoomBase(z) + d;
    }

    /// <summary>The tile whose id is <paramref name="tileId"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id names a tile deeper than <see cref="TileAddress.MaxZoom"/>.</exception>
    public static TileAddress ToTile(ulong tileId)
    {
        for (var z = TileAddress.MinZoom; z <= TileAddress.MaxZoom; z++)
        {
            var count = 1UL << (2 * z);
            var zoomBase = ZoomBase(z);
            if (tileId >= zoomBase + count)
                continue;

            var position = (long)(tileId - zoomBase);
            var n = 1L << z;
            long x = 0, y = 0;
            for (var s = 1L; s < n; s *= 2)
            {
                var rx = 1 & (position / 2);
                var ry = 1 & (position ^ rx);
                Rotate(s, ref x, ref y, rx, ry);
                x += s * rx;
                y += s * ry;
                position /= 4;
            }

            return new TileAddress(z, (int)x, (int)y);
        }

        throw new ArgumentOutOfRangeException(nameof(tileId), tileId, $"The tile id names a tile deeper than zoom {TileAddress.MaxZoom}.");
    }

    private static ulong ZoomBase(int z) => ((1UL << (2 * z)) - 1) / 3;

    private static void Rotate(long n, ref long x, ref long y, long rx, long ry)
    {
        if (ry != 0)
            return;

        if (rx == 1)
        {
            x = n - 1 - x;
            y = n - 1 - y;
        }

        (x, y) = (y, x);
    }
}
