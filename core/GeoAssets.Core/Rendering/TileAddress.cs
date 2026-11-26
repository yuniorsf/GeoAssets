using System.Globalization;
using System.Runtime.InteropServices;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// One tile of the EPSG:3857 (Web Mercator) tile pyramid, in the XYZ scheme MapLibre, Leaflet and PostGIS
/// <c>ST_TileEnvelope</c> use (ADR-002 D3/D6, XD01-431): at zoom <see cref="Z"/> the square world is cut into
/// <c>2^Z × 2^Z</c> tiles, column <see cref="X"/> counted east from the antimeridian (180°W) and row <see cref="Y"/>
/// counted south from the north edge (≈ 85.0511°N). Always valid: the constructor refuses a zoom outside
/// [<see cref="MinZoom"/>, <see cref="MaxZoom"/>] or a column/row outside <c>[0, 2^Z)</c>; <c>default</c> is the
/// world tile <c>0/0/0</c>. A tile covers its west and north edges, not its east and south ones — except the last
/// column and row, which reach the world's edge (<see cref="Containing"/>, <see cref="TileRange.Covering"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TileAddress
{
    /// <summary>The lowest zoom: one tile for the whole world.</summary>
    public const int MinZoom = AssetTypeZoomRange.LowestZoom;

    /// <summary>The highest zoom a tile may have — the same bound asset-type zoom ranges and LOD thresholds use.</summary>
    public const int MaxZoom = AssetTypeZoomRange.HighestZoom;

    /// <summary>The tile at zoom <paramref name="z"/>, column <paramref name="x"/>, row <paramref name="y"/> (XYZ).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The zoom, column or row is outside the pyramid.</exception>
    public TileAddress(int z, int x, int y)
    {
        EnsureZoom(z, nameof(z));
        var n = 1 << z;
        if ((uint)x >= (uint)n)
            throw new ArgumentOutOfRangeException(nameof(x), x, string.Create(CultureInfo.InvariantCulture, $"The tile column must be in [0, {n}) at zoom {z}."));
        if ((uint)y >= (uint)n)
            throw new ArgumentOutOfRangeException(nameof(y), y, string.Create(CultureInfo.InvariantCulture, $"The tile row must be in [0, {n}) at zoom {z}."));

        Z = z;
        X = x;
        Y = y;
    }

    /// <summary>The zoom level.</summary>
    public int Z { get; }

    /// <summary>The column, counted east from the antimeridian.</summary>
    public int X { get; }

    /// <summary>The row in the XYZ scheme, counted south from the north edge.</summary>
    public int Y { get; }

    /// <summary>The row in the TMS scheme (counted north from the south edge, as MBTiles stores it).</summary>
    public int TmsY => TilesPerAxis(Z) - 1 - Y;

    /// <summary>The tile's EPSG:3857 envelope in meters — the same box as PostGIS <c>ST_TileEnvelope(Z, X, Y)</c>.</summary>
    public MercatorBounds Envelope3857
    {
        get
        {
            var n = TilesPerAxis(Z);
            var size = 2 * WebMercator.HalfWorldSize;
            return new MercatorBounds(
                -WebMercator.HalfWorldSize + (size * X / n),
                WebMercator.HalfWorldSize - (size * (Y + 1) / n),
                -WebMercator.HalfWorldSize + (size * (X + 1) / n),
                WebMercator.HalfWorldSize - (size * Y / n));
        }
    }

    /// <summary>The tile's envelope in longitude/latitude degrees (EPSG:4326), within ±180° and ±<see cref="WebMercator.MaxLatitude"/>.</summary>
    public BoundingBox Envelope4326 => WebMercator.Unproject(Envelope3857);

    /// <summary>The tile one zoom out that contains this one; <c>null</c> at zoom 0.</summary>
    public TileAddress? Parent => Z == MinZoom ? null : Ancestor(Z - 1);

    /// <summary>The number of columns (and rows) at <paramref name="zoom"/>: <c>2^zoom</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</exception>
    public static int TilesPerAxis(int zoom)
    {
        EnsureZoom(zoom, nameof(zoom));
        return 1 << zoom;
    }

    /// <summary>Whether (<paramref name="z"/>, <paramref name="x"/>, <paramref name="y"/>) names a tile of the pyramid.</summary>
    public static bool IsValid(int z, int x, int y) =>
        z is >= MinZoom and <= MaxZoom && (uint)x < (uint)(1 << z) && (uint)y < (uint)(1 << z);

    /// <summary>The tile at (<paramref name="z"/>, <paramref name="x"/>, <paramref name="y"/>), or <c>false</c> when there is none.</summary>
    public static bool TryCreate(int z, int x, int y, out TileAddress address)
    {
        if (!IsValid(z, x, y))
        {
            address = default;
            return false;
        }

        address = new TileAddress(z, x, y);
        return true;
    }

    /// <summary>The tile a TMS row (<paramref name="tmsY"/>, counted north from the south edge) names.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The zoom, column or row is outside the pyramid.</exception>
    public static TileAddress FromTms(int z, int x, int tmsY)
    {
        EnsureZoom(z, nameof(z));
        var n = 1 << z;
        if ((uint)tmsY >= (uint)n)
            throw new ArgumentOutOfRangeException(nameof(tmsY), tmsY, string.Create(CultureInfo.InvariantCulture, $"The tile row must be in [0, {n}) at zoom {z}."));
        return new TileAddress(z, x, n - 1 - tmsY);
    }

    /// <summary>
    /// The tile at <paramref name="zoom"/> that contains the point — clamped to the world first, so a longitude of
    /// ±180 (or beyond) falls in the first/last column and a latitude beyond ±<see cref="WebMercator.MaxLatitude"/>
    /// (a pole) in the first/last row.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is outside the pyramid.</exception>
    public static TileAddress Containing(double longitude, double latitude, int zoom)
    {
        var n = TilesPerAxis(zoom);
        return new TileAddress(
            zoom,
            ClampToGrid(WebMercator.TileFractionX(longitude, n), n),
            ClampToGrid(WebMercator.TileFractionY(latitude, n), n));
    }

    /// <summary>The tile at <paramref name="zoom"/> (at most <see cref="Z"/>) that contains this one.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is negative or above <see cref="Z"/>.</exception>
    public TileAddress Ancestor(int zoom)
    {
        if (zoom < MinZoom || zoom > Z)
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, string.Create(CultureInfo.InvariantCulture, $"An ancestor's zoom must be in [{MinZoom}, {Z}]."));

        var shift = Z - zoom;
        return new TileAddress(zoom, X >> shift, Y >> shift);
    }

    /// <summary>The tiles at <paramref name="zoom"/> (at least <see cref="Z"/>) this one splits into; <c>Z + 1</c> gives its four children.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoom"/> is below <see cref="Z"/> or above <see cref="MaxZoom"/>.</exception>
    public TileRange Descendants(int zoom)
    {
        if (zoom < Z || zoom > MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, string.Create(CultureInfo.InvariantCulture, $"A descendant's zoom must be in [{Z}, {MaxZoom}]."));

        var shift = zoom - Z;
        return new TileRange(zoom, X << shift, Y << shift, ((X + 1) << shift) - 1, ((Y + 1) << shift) - 1);
    }

    /// <summary>
    /// The aligned <paramref name="span"/> × <paramref name="span"/> block of tiles at this zoom that contains this one —
    /// what a metatile renderer draws in one pass and cuts (a 2 × 2 metatile is <c>Metatile(2)</c>, the four children of
    /// <see cref="Parent"/>); cut short at the world's east and south edges, so at zoom 0 it is the world tile alone.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is less than 1.</exception>
    public TileRange Metatile(int span)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span, 1);

        var last = TilesPerAxis(Z) - 1;
        var minX = X / span * span;
        var minY = Y / span * span;
        return new TileRange(Z, minX, minY, (int)Math.Min((long)minX + span - 1, last), (int)Math.Min((long)minY + span - 1, last));
    }

    /// <summary>The tile's path, <c>z/x/y</c>, in invariant digits — the form tile URLs and cache keys use.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Z}/{X}/{Y}");

    internal static void EnsureZoom(int zoom, string paramName)
    {
        if (zoom is < MinZoom or > MaxZoom)
            throw new ArgumentOutOfRangeException(paramName, zoom, string.Create(CultureInfo.InvariantCulture, $"The zoom must be in [{MinZoom}, {MaxZoom}]."));
    }

    internal static int ClampToGrid(double fraction, int tilesPerAxis) =>
        (int)Math.Clamp(Math.Floor(fraction), 0d, tilesPerAxis - 1d);
}
