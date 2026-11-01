using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_service_orders")]
internal sealed class LocalServiceOrderRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string OrderJson { get; set; } = string.Empty;
}
