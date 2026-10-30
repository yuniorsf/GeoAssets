using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

public sealed class RecordingOutboxDispatcherStore : IOutboxDispatcherStore
{
    private readonly Queue<OutboxPendingMessage> _pending = new();
    private readonly List<(int BatchSize, TimeSpan LeaseDuration, string WorkerId)> _claimCalls = [];
    private readonly List<(Guid MessageId, string WorkerId)> _dispatchedCalls = [];
    private readonly List<(Guid MessageId, string WorkerId, string ErrorMessage, TimeSpan RetryDelay, int MaxAttempts)> _failedCalls = [];

    public IReadOnlyList<(int BatchSize, TimeSpan LeaseDuration, string WorkerId)> ClaimCalls => _claimCalls;

    public IReadOnlyList<(Guid MessageId, string WorkerId)> DispatchedCalls => _dispatchedCalls;

    public IReadOnlyList<(Guid MessageId, string WorkerId, string ErrorMessage, TimeSpan RetryDelay, int MaxAttempts)> FailedCalls => _failedCalls;

    public int ReleaseExpiredLeasesCallCount { get; private set; }

    public void Seed(OutboxPendingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _pending.Enqueue(message);
    }

    public Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _claimCalls.Add((batchSize, leaseDuration, workerId));

        var claimed = new List<OutboxPendingMessage>();
        while (claimed.Count < batchSize && _pending.TryDequeue(out var next))
        {
            claimed.Add(next);
        }

        return Task.FromResult<IReadOnlyList<OutboxPendingMessage>>(claimed);
    }

    public Task MarkDispatchedAsync(Guid messageId, string workerId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _dispatchedCalls.Add((messageId, workerId));
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        Guid messageId,
        string workerId,
        string errorMessage,
        TimeSpan retryDelay,
        int maxAttempts,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _failedCalls.Add((messageId, workerId, errorMessage, retryDelay, maxAttempts));
        return Task.CompletedTask;
    }

    public Task<int> ReleaseExpiredLeasesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ReleaseExpiredLeasesCallCount++;
        return Task.FromResult(0);
    }
}
