using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

public sealed class RecordingIdempotentInbox : IIdempotentInbox
{
    private readonly HashSet<(Guid MessageId, string ConsumerName)> _processed = new(new CompositeInboxKeyComparer());
    private readonly List<(Guid MessageId, string ConsumerName)> _hasProcessedCalls = [];
    private readonly List<(Guid MessageId, string ConsumerName, string MessageType, TimeSpan RetentionPeriod)> _recordedEntries = [];

    public IReadOnlyList<(Guid MessageId, string ConsumerName)> HasProcessedCalls => _hasProcessedCalls;

    public IReadOnlyList<(Guid MessageId, string ConsumerName, string MessageType, TimeSpan RetentionPeriod)> RecordedEntries => _recordedEntries;

    public int PurgeCallCount { get; private set; }

    public void SeedProcessed(Guid messageId, string consumerName)
    {
        _processed.Add((messageId, consumerName));
    }

    public Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _hasProcessedCalls.Add((messageId, consumerName));
        return Task.FromResult(_processed.Contains((messageId, consumerName)));
    }

    public Task<bool> TryRecordProcessedAsync(
        Guid messageId,
        string consumerName,
        string messageType,
        TimeSpan retentionPeriod,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_processed.Add((messageId, consumerName)))
        {
            return Task.FromResult(false);
        }

        _recordedEntries.Add((messageId, consumerName, messageType, retentionPeriod));
        return Task.FromResult(true);
    }

    public Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        PurgeCallCount++;
        return Task.FromResult(0);
    }

    private sealed class CompositeInboxKeyComparer : IEqualityComparer<(Guid MessageId, string ConsumerName)>
    {
        public bool Equals((Guid MessageId, string ConsumerName) x, (Guid MessageId, string ConsumerName) y) =>
            x.MessageId == y.MessageId && string.Equals(x.ConsumerName, y.ConsumerName, StringComparison.Ordinal);

        public int GetHashCode((Guid MessageId, string ConsumerName) obj) =>
            HashCode.Combine(obj.MessageId, StringComparer.Ordinal.GetHashCode(obj.ConsumerName));
    }
}
