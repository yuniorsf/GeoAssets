using GeoAssets.Shared.Services.Offline;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>SQLCipher-backed <see cref="ILocalMediaStagingStore"/> (XD01-243).</summary>
internal sealed class SqliteLocalMediaStagingStore : ILocalMediaStagingStore
{
    public async Task StageAsync(LocalMediaStagingEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalMediaStagingRow
        {
            Id = entry.Id.ToString("D"),
            ServiceOrderId = entry.ServiceOrderId,
            LocalFilePath = entry.LocalFilePath,
            CapturedAtUtc = entry.CapturedAtUtc,
            UploadAttemptCount = entry.UploadAttemptCount,
            Uploaded = entry.Uploaded,
        };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalMediaStagingEntry>> GetPendingUploadsAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.Table<LocalMediaStagingRow>().Where(r => !r.Uploaded).ToListAsync().ConfigureAwait(false);
        return rows
            .Select(r => new LocalMediaStagingEntry(
                Guid.Parse(r.Id), r.ServiceOrderId, r.LocalFilePath, r.CapturedAtUtc, r.UploadAttemptCount, r.Uploaded))
            .ToList();
    }

    public async Task MarkUploadedAsync(Guid entryId, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalMediaStagingRow>(entryId.ToString("D")).ConfigureAwait(false);
        if (row is null) return;

        row.Uploaded = true;
        await db.UpdateAsync(row).ConfigureAwait(false);
    }

    public async Task MarkUploadFailedAsync(Guid entryId, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalMediaStagingRow>(entryId.ToString("D")).ConfigureAwait(false);
        if (row is null) return;

        row.UploadAttemptCount += 1;
        await db.UpdateAsync(row).ConfigureAwait(false);
    }
}
