using GeoAssets.Shared.Services.Offline;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// SQLCipher-backed <see cref="ILocalActionOutbox"/> (XD01-243).
/// <see cref="NextSequenceNumberAsync"/> derives the next value from the table itself
/// (<c>MAX(SequenceNumber) + 1</c>) rather than a separate counter row — correct for a
/// single-writer mobile client (one app process, no concurrent writers to race).
/// </summary>
internal sealed class SqliteLocalActionOutbox : ILocalActionOutbox
{
    public async Task<long> NextSequenceNumberAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var max = await db.ExecuteScalarAsync<long>(
            "SELECT COALESCE(MAX(SequenceNumber), 0) FROM local_action_outbox").ConfigureAwait(false);
        return max + 1;
    }

    public async Task EnqueueAsync(LocalOutboxEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = new LocalOutboxRow
        {
            Id = entry.Id.ToString("D"),
            SequenceNumber = entry.SequenceNumber,
            ServiceOrderId = entry.ServiceOrderId,
            ServiceOrderJson = entry.ServiceOrderJson,
            CreatedAtUtc = entry.CreatedAtUtc,
            AttemptCount = entry.AttemptCount,
            LastError = entry.LastError,
            Dispatched = false,
        };
        await db.InsertOrReplaceAsync(row).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalOutboxEntry>> GetPendingInOrderAsync(CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var rows = await db.Table<LocalOutboxRow>()
            .Where(r => !r.Dispatched)
            .OrderBy(r => r.SequenceNumber)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows
            .Select(r => new LocalOutboxEntry(
                Guid.Parse(r.Id), r.SequenceNumber, r.ServiceOrderId, r.ServiceOrderJson,
                r.CreatedAtUtc, r.AttemptCount, r.LastError))
            .ToList();
    }

    public async Task MarkDispatchedAsync(Guid entryId, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalOutboxRow>(entryId.ToString("D")).ConfigureAwait(false);
        if (row is null) return;

        row.Dispatched = true;
        await db.UpdateAsync(row).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(Guid entryId, string error, CancellationToken ct = default)
    {
        var db = await LocalOfflineDatabase.GetConnectionAsync().ConfigureAwait(false);
        var row = await db.FindAsync<LocalOutboxRow>(entryId.ToString("D")).ConfigureAwait(false);
        if (row is null) return;

        row.AttemptCount += 1;
        row.LastError = error;
        await db.UpdateAsync(row).ConfigureAwait(false);
    }
}
