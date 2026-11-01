using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("cto_port_cache")]
internal sealed class LocalCtoPortCacheRow
{
    [PrimaryKey]
    public string CtoFeatureId { get; set; } = string.Empty;

    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public int TotalPorts { get; set; }
    public int OccupiedPorts { get; set; }
    public DateTime CachedAtUtc { get; set; }
}
