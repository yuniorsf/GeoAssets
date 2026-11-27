using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_vector_tiles")]
internal sealed class LocalVectorTileRow
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public int Z { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public byte[] Data { get; set; } = [];
    public DateTime CachedAtUtc { get; set; }

    /// <summary>The tile's media type — set for <c>ITileCache</c> entries (XD01-438), empty for the z/x/y basemap ones.</summary>
    public string? ContentType { get; set; }

    /// <summary>The tile's <c>Content-Encoding</c> (e.g. <c>gzip</c>) — <c>ITileCache</c> entries only; empty when stored uncompressed.</summary>
    public string? ContentEncoding { get; set; }
}
