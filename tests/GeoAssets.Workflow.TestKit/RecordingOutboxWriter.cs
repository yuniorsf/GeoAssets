using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// An <see cref="IOutboxWriter"/> that keeps what was staged, for tests of services that stage
/// integration events (XD01-316). <see cref="OnEnqueueAsync"/> runs before a message is kept, so
/// a test can fail or cancel a particular enqueue.
/// </summary>
public sealed class RecordingOutboxWriter : IOutboxWriter
{
    private readonly List<StagedOutboxMessage> _messages = [];

    /// <summary>Every typed message staged, in order.</summary>
    public IReadOnlyList<StagedOutboxMessage> Messages => _messages;

    /// <summary>Every raw envelope staged, in order.</summary>
    public List<IntegrationEnvelope> Envelopes { get; } = [];

    /// <summary>Called with each typed message before it is kept; throwing here fails the enqueue.</summary>
    public Func<StagedOutboxMessage, CancellationToken, Task>? OnEnqueueAsync { get; set; }

    public async Task<Guid> EnqueueAsync<TEvent>(
        TEvent message,
        string messageType,
        string partitionKey,
        string? catalogInstanceId = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken ct = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ct.ThrowIfCancellationRequested();

        var staged = new StagedOutboxMessage(Guid.NewGuid(), message, messageType, partitionKey, correlationId, causationId);
        if (OnEnqueueAsync is not null)
            await OnEnqueueAsync(staged, ct);

        _messages.Add(staged);
        return staged.MessageId;
    }

    public Task EnqueueEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        Envelopes.Add(envelope);
        return Task.CompletedTask;
    }
}
