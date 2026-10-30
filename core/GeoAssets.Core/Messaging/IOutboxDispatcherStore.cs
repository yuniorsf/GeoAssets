namespace GeoAssets.Core.Messaging;

/// <summary>
/// Lease-based store contract consumed by the background outbox relay worker.
/// </summary>
public interface IOutboxDispatcherStore
{
    Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct = default);

    Task MarkDispatchedAsync(Guid messageId, string workerId, CancellationToken ct = default);

    Task MarkFailedAsync(
        Guid messageId,
        string workerId,
        string errorMessage,
        TimeSpan retryDelay,
        int maxAttempts,
        CancellationToken ct = default);

    Task<int> ReleaseExpiredLeasesAsync(CancellationToken ct = default);
}
