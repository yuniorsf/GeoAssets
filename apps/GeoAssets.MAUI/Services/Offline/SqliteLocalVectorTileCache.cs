using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services.Offline;
using SQLite;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// SQLCipher-backed <see cref="ILocalVectorTileCache"/> (XD01-243) — opportunistic cache only;
/// see <see cref="ILocalVectorTileCache"/>'s doc comment for why pre-seeding a work area is out
/// of this ticket's scope.
/// <para>
/// Also the app's <see cref="ITileCache"/> (XD01-438): the tiles of an offline map pack (the <c>pmtiles</c> provider)
/// are read through it — a tile is looked up here first and, read from the pack on a miss, kept here as stored (its
/// bytes, media type and encoding) under its <see cref="TileCacheKey.Value"/>, in the same encrypted table as the
/// z/x/y basemap entries (whose keys, <c>z:x:y</c>, never collide with a <c>tile/…</c> key). Bounded: once more than
/// <c>maxTileEntries</c> (<see cref="MaxTileEntries"/> by default) <c>tile/…</c> entries are kept, checked every
/// <c>pruneInterval</c> writes, the oldest written are deleted down to it. <see cref="RemoveAsync"/> evicts the
/// <c>tile/…</c> entries of a tile range. sqlite-net takes no <see cref="CancellationToken"/>, so a cancelled call stops
/// before its statement.
/// </para>
/// </summary>
internal sealed class SqliteLocalVectorTileCache : ILocalVectorTileCache, ITileCache
{
    /// <summary>How many <see cref="ITileCache"/> entries are kept by default.</summary>
    public const int MaxTileEntries = 20_000;

    /// <summary>How many <see cref="ITileCache"/> writes pass between two size checks, by default.</summary>
    public const int DefaultPruneInterval = 256;

    private const string Table = "local_vector_tiles";
    private const string TileKeyPattern = TileCacheKey.KeyPrefix + "/%";

    private readonly Func<Task<SQLiteAsyncConnection>> _connection;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxTileEntries;
    private readonly int _pruneInterval;
    private int _writesSincePrune;

    /// <summary>A cache over the database <paramref name="connection"/> opens (the app's <c>LocalOfflineDatabase</c>).</summary>
    public SqliteLocalVectorTileCache(
        Func<Task<SQLiteAsyncConnection>> connection,
        TimeProvider timeProvider,
        int maxTileEntries = MaxTileEntries,
        int pruneInterval = DefaultPruneInterval)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTileEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pruneInterval);
        _connection = connection;
        _timeProvider = timeProvider;
        _maxTileEntries = maxTileEntries;
        _pruneInterval = pruneInterval;
    }

    private static string RowKey(int z, int x, int y) => $"{z}:{x}:{y}";

    public async Task<LocalVectorTile?> GetAsync(int z, int x, int y, CancellationToken ct = default)
    {
        var db = await OpenAsync(ct).ConfigureAwait(false);
        var row = await db.FindAsync<LocalVectorTileRow>(RowKey(z, x, y)).ConfigureAwait(false);
        return row is null ? null : new LocalVectorTile(row.Z, row.X, row.Y, row.Data, row.CachedAtUtc);
    }

    public async Task PutAsync(LocalVectorTile tile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var db = await OpenAsync(ct).ConfigureAwait(false);
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

    public async Task<TilePayload?> GetAsync(TileCacheKey key, CancellationToken ct)
    {
        var db = await OpenAsync(ct).ConfigureAwait(false);
        var row = await db.FindAsync<LocalVectorTileRow>(key.Value).ConfigureAwait(false);
        if (row is null)
            return null;

        return new TilePayload(
            row.Data,
            string.IsNullOrWhiteSpace(row.ContentType) ? key.Format.ContentType() : row.ContentType,
            string.IsNullOrWhiteSpace(row.ContentEncoding) ? null : row.ContentEncoding);
    }

    public async Task SetAsync(TileCacheKey key, TilePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var db = await OpenAsync(ct).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(new LocalVectorTileRow
        {
            Key = key.Value,
            Z = key.Address.Z,
            X = key.Address.X,
            Y = key.Address.Y,
            Data = payload.Content.ToArray(),
            CachedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            ContentType = payload.ContentType,
            ContentEncoding = payload.ContentEncoding,
        }).ConfigureAwait(false);

        if (Interlocked.Increment(ref _writesSincePrune) >= _pruneInterval)
        {
            Interlocked.Exchange(ref _writesSincePrune, 0);
            await PruneAsync(db).ConfigureAwait(false);
        }
    }

    public async Task RemoveAsync(TileRange range, CancellationToken ct)
    {
        var db = await OpenAsync(ct).ConfigureAwait(false);
        await db.ExecuteAsync(
            $"DELETE FROM {Table} WHERE Key LIKE ? AND Z = ? AND X >= ? AND X <= ? AND Y >= ? AND Y <= ?",
            TileKeyPattern, range.Zoom, range.MinX, range.MaxX, range.MinY, range.MaxY).ConfigureAwait(false);
    }

    private async Task PruneAsync(SQLiteAsyncConnection db)
    {
        var count = await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Table} WHERE Key LIKE ?", TileKeyPattern).ConfigureAwait(false);
        if (count <= _maxTileEntries)
            return;

        await db.ExecuteAsync(
            $"DELETE FROM {Table} WHERE Key IN (SELECT Key FROM {Table} WHERE Key LIKE ? ORDER BY CachedAtUtc, Key LIMIT ?)",
            TileKeyPattern, count - _maxTileEntries).ConfigureAwait(false);
    }

    private Task<SQLiteAsyncConnection> OpenAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _connection();
    }
}
