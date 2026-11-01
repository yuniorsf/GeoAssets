using GeoAssets.Shared.Services.Offline;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// SQLCipher-backed <see cref="ILocalVectorTileCache"/> (XD01-243) — opportunistic cache only;
/// see <see cref="ILocalVectorTileCache"/>'s doc comment for why pre-seeding a work area is out
/// of this ticket's scope.
/// </summary>
internal sealed class SqliteLocalVectorTileCache : ILocalVectorTileCache
{
    private static string RowKey(int z, int x, int y) => $"{z}:{x}:{y}";

    public async Task<LocalVectorTile?> GetAsync(int z, int x, int y, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalVectorTileRow>(RowKey(z, x, y)).ConfigureAwait(false);
        return row is null ? null : new LocalVectorTile(row.Z, row.X, row.Y, row.Data, row.CachedAtUtc);
    }

    public async Task PutAsync(LocalVectorTile tile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalVectorTileRow
        {
            Key = RowKey(tile.Z, tile.X, tile.Y),
            Z = tile.Z,
            X = tile.X,
            Y = tile.Y,
            Data = tile.Data,
            CachedAtUtc = tile.CachedAtUtc,
        };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }
}
