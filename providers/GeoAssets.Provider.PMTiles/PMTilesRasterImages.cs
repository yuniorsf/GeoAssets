using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles;

/// <summary>The raster tile types this reader serves — PNG, JPEG and WebP — their media types and file signatures (XD01-465).</summary>
public static class PMTilesRasterImages
{
    /// <summary>The media type of a JPEG tile.</summary>
    public const string JpegContentType = "image/jpeg";

    /// <summary>The media type of a WebP tile.</summary>
    public const string WebpContentType = "image/webp";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> RiffTag => "RIFF"u8;

    private static ReadOnlySpan<byte> WebpTag => "WEBP"u8;

    /// <summary>Whether tiles of <paramref name="tileType"/> are images this reader serves (PNG, JPEG, WebP).</summary>
    public static bool IsSupported(PMTilesTileType tileType) =>
        tileType is PMTilesTileType.Png or PMTilesTileType.Jpeg or PMTilesTileType.Webp;

    /// <summary>The media type of a tile of <paramref name="tileType"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tileType"/> is not PNG, JPEG or WebP.</exception>
    public static string ContentType(PMTilesTileType tileType) => tileType switch
    {
        PMTilesTileType.Png => TileFormats.PngContentType,
        PMTilesTileType.Jpeg => JpegContentType,
        PMTilesTileType.Webp => WebpContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(tileType), tileType, "Not a PNG, JPEG or WebP tile type."),
    };

    /// <summary>Whether <paramref name="image"/> starts with the file signature of <paramref name="tileType"/>.</summary>
    public static bool HasSignature(PMTilesTileType tileType, ReadOnlySpan<byte> image) => tileType switch
    {
        PMTilesTileType.Png => image.StartsWith(PngSignature),
        PMTilesTileType.Jpeg => image.StartsWith(JpegSignature),
        PMTilesTileType.Webp => image.Length >= 12 && image.StartsWith(RiffTag) && image[8..12].SequenceEqual(WebpTag),
        _ => false,
    };
}
