namespace GeoAssets.Core.Rendering;

/// <summary>The media type and URL extension of each <see cref="TileFormat"/> (XD01-431).</summary>
public static class TileFormats
{
    /// <summary>The media type of a Mapbox Vector Tile.</summary>
    public const string MapboxVectorTileContentType = "application/vnd.mapbox-vector-tile";

    /// <summary>The media type of a PNG tile.</summary>
    public const string PngContentType = "image/png";

    /// <summary>The media type a tile of <paramref name="format"/> is served with.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined <see cref="TileFormat"/>.</exception>
    public static string ContentType(this TileFormat format) => format switch
    {
        TileFormat.Mvt => MapboxVectorTileContentType,
        TileFormat.Png => PngContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown tile format."),
    };

    /// <summary>The URL / cache-key extension of a tile of <paramref name="format"/>, without the dot (<c>mvt</c>, <c>png</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a defined <see cref="TileFormat"/>.</exception>
    public static string FileExtension(this TileFormat format) => format switch
    {
        TileFormat.Mvt => "mvt",
        TileFormat.Png => "png",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown tile format."),
    };
}
