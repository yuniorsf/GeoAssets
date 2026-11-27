using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>Small offline map packs for the pack tests: <see cref="PMTilesTestArchive"/> archives of <see cref="TestMvt"/> tiles.</summary>
internal static class TestPacks
{
    /// <summary>A gzip vector tile with one point named <paramref name="name"/> at the tile's center, in layer <paramref name="layer"/>.</summary>
    public static byte[] PointTile(string name, string layer = "pois") =>
        PMTilesTestArchive.Gzip(TestMvt.Encode(new TestMvt.Layer(layer,
            [TestMvt.PointAt(2048, 2048, new Dictionary<string, object>(StringComparer.Ordinal) { ["name"] = name })])));

    /// <summary>
    /// A pack (zooms 0–<paramref name="maxZoom"/>) with a named point in the tile at its center for each listed tile —
    /// named after the tile, e.g. <c>"3/4/2"</c>.
    /// </summary>
    public static string Write(string directory, IEnumerable<TileAddress> tiles, byte maxZoom = 14, byte minZoom = 0,
        (double West, double South, double East, double North)? bounds = null)
    {
        var archive = new PMTilesTestArchive
        {
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            Bounds = bounds ?? (-180, -85, 180, 85),
            MetadataJson = """{"vector_layers":[{"id":"pois"}]}""",
        };
        foreach (var tile in tiles)
            archive.Add(tile, PointTile(tile.ToString()));
        return archive.WriteTo(directory);
    }
}
