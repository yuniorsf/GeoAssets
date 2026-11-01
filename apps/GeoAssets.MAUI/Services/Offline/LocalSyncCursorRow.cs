using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

[Table("local_sync_cursor")]
internal sealed class LocalSyncCursorRow
{
    [PrimaryKey]
    public string Id { get; set; } = CursorRowId;

    public DateTime LastSyncedUtc { get; set; }

    internal const string CursorRowId = "cursor";
}
