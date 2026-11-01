using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_service_order_base_snapshots")]
internal sealed class LocalBaseSnapshotRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string OrderJson { get; set; } = string.Empty;
}
