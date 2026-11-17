using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

public sealed class RecordingOutboxDispatcherStore : IOutboxDispatcherStore
{
    private readonly List<OutboxPendingMessage> _pending = [];
    private readonly List<(int BatchSize, TimeSpan LeaseDuration, string WorkerId)> _claimCalls = [];
    private readonly List<IReadOnlyCollection<string>> _claimedMessageTypes = [];
    private readonly List<(Guid MessageId, string WorkerId)> _dispatchedCalls = [];
    private readonly List<(Guid MessageId, string WorkerId, string ErrorMessage, TimeSpan RetryDelay, int MaxAttempts)> _failedCalls = [];

    public IReadOnlyList<(int BatchSize, TimeSpan LeaseDuration, string WorkerId)> ClaimCalls => _claimCalls;

    /// <summary>The message types each claim asked for, in call order; an empty entry is a claim of every type.</summary>
    public IReadOnlyList<IReadOnlyCollection<string>> ClaimedMessageTypes => _claimedMessageTypes;

    public IReadOnlyList<(Guid MessageId, string WorkerId)> DispatchedCalls => _dispatchedCalls;

    public IReadOnlyList<(Guid MessageId, string WorkerId, string ErrorMessage, TimeSpan RetryDelay, int MaxAttempts)> FailedCalls => _failedCalls;

    public int ReleaseExpiredLeasesCallCount { get; private set; }

    public void Seed(OutboxPendingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _pending.Add(message);
    }

    public Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct = default) =>
        ClaimPendingBatchAsync(batchSize, leaseDuration, workerId, [], ct);

    public Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        IReadOnlyCollection<string> messageTypes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messageTypes);
        ct.ThrowIfCancellationRequested();
        _claimCalls.Add((batchSize, leaseDuration, workerId));
        _claimedMessageTypes.Add(messageTypes);

        var claimed = _pending
            .Where(message => messageTypes.Count == 0 || messageTypes.Contains(message.Envelope.MessageType, StringComparer.Ordinal))
            .Take(batchSize)
            .ToList();
        foreach (var message in claimed)
        {
            _pending.Remove(message);
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
