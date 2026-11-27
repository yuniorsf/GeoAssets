using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>Tiny raster tile images and raster archives for the XD01-465 tests, generated here rather than checked in.</summary>
internal static class TestRasterTiles
{
    /// <summary>A 1 × 1 PNG.</summary>
    public static byte[] Png { get; } = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>A 1 × 1 lossy WebP.</summary>
    public static byte[] Webp { get; } = Convert.FromBase64String("UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    /// <summary>The start of a JPEG (SOI + APP0 marker) — enough for the reader, which never decodes an image.</summary>
    public static byte[] Jpeg { get; } = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0xFF, 0xD9];

    /// <summary>The image of <paramref name="tileType"/>.</summary>
    public static byte[] ImageOf(PMTilesTileType tileType) => tileType switch
    {
        PMTilesTileType.Png => Png,
        PMTilesTileType.Webp => Webp,
        PMTilesTileType.Jpeg => Jpeg,
        _ => throw new ArgumentOutOfRangeException(nameof(tileType), tileType, null),
    };

    /// <summary>
    /// An uncompressed raster archive of <paramref name="tileType"/> images (zooms <paramref name="minZoom"/>–<paramref name="maxZoom"/>)
    /// with the image at every listed tile.
    /// </summary>
    public static PMTilesTestArchive Archive(PMTilesTileType tileType, IEnumerable<TileAddress> tiles, byte minZoom = 0, byte maxZoom = 14,
        (double West, double South, double East, double North)? bounds = null)
    {
        var archive = new PMTilesTestArchive
        {
            TileType = tileType,
            TileCompression = PMTilesCompression.None,
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            Bounds = bounds ?? (-180, -85, 180, 85),
            MetadataJson = """{"format":"raster"}""",
        };
        foreach (var tile in tiles)
            archive.Add(tile, ImageOf(tileType));
        return archive;
    }
}
