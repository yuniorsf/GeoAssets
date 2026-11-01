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
}
