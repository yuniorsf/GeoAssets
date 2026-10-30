namespace GeoAssets.Core.Messaging;

/// <summary>
/// Tracks processed integration message identifiers per logical consumer to guarantee idempotent handling.
/// </summary>
public interface IIdempotentInbox
{
    Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);

    Task<bool> TryRecordProcessedAsync(
        Guid messageId,
        string consumerName,
        string messageType,
        TimeSpan retentionPeriod,
        CancellationToken ct = default);

    Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default);
}
