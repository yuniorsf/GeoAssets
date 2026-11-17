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

    /// <summary>
    /// Same claim as the overload without <paramref name="messageTypes"/>, restricted to messages
    /// whose <see cref="IntegrationEnvelope.MessageType"/> is one of <paramref name="messageTypes"/>
    /// (ordinal); an empty collection claims every type. Messages of other types are left exactly
    /// as they are — pending, unleased, attempts untouched — for whichever relay does deliver them
    /// (XD01-337).
    /// </summary>
    Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        IReadOnlyCollection<string> messageTypes,
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
