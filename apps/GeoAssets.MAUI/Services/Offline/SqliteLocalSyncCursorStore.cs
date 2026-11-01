using GeoAssets.Shared.Services.Offline;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// SQLCipher-backed <see cref="ILocalSyncCursorStore"/> (XD01-243) — a single fixed-key row
/// (one device, one sync cursor).
/// </summary>
internal sealed class SqliteLocalSyncCursorStore : ILocalSyncCursorStore
{
    public async Task<DateTime> GetLastSyncedUtcAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalSyncCursorRow>(LocalSyncCursorRow.CursorRowId).ConfigureAwait(false);
        return row?.LastSyncedUtc ?? DateTime.MinValue;
    }

    public async Task SetLastSyncedUtcAsync(DateTime lastSyncedUtc, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalSyncCursorRow { Id = LocalSyncCursorRow.CursorRowId, LastSyncedUtc = lastSyncedUtc };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }
}
