using GeoAssets.Shared.Services.Offline;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>SQLCipher-backed <see cref="ILocalCtoPortCacheStore"/> (XD01-276).</summary>
internal sealed class SqliteLocalCtoPortCacheStore : ILocalCtoPortCacheStore
{
    public async Task<IReadOnlyList<LocalCtoPortCacheEntry>> GetAllAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.Table<LocalCtoPortCacheRow>().ToListAsync().ConfigureAwait(false);
        return rows
            .Select(r => new LocalCtoPortCacheEntry(r.CtoFeatureId, r.Longitude, r.Latitude, r.TotalPorts, r.OccupiedPorts, r.CachedAtUtc))
            .ToList();
    }

    public async Task PutAsync(LocalCtoPortCacheEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalCtoPortCacheRow
        {
            CtoFeatureId = entry.CtoFeatureId,
            Longitude = entry.Longitude,
            Latitude = entry.Latitude,
            TotalPorts = entry.TotalPorts,
            OccupiedPorts = entry.OccupiedPorts,
            CachedAtUtc = entry.CachedAtUtc,
        };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }
}
