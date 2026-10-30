using System.Diagnostics;
using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.TestKit;

public sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly List<IntegrationEnvelope> _publishedEnvelopes = [];
    private readonly List<IReadOnlyList<IntegrationEnvelope>> _publishedBatches = [];
    private readonly List<ActivityContext?> _observedActivityContexts = [];

    public IReadOnlyList<IntegrationEnvelope> PublishedEnvelopes => _publishedEnvelopes;

    public IReadOnlyList<IReadOnlyList<IntegrationEnvelope>> PublishedBatches => _publishedBatches;

    public IReadOnlyList<ActivityContext?> ObservedActivityContexts => _observedActivityContexts;

    public Func<IntegrationEnvelope, CancellationToken, Task>? OnPublishAsync { get; set; }

    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        _observedActivityContexts.Add(Activity.Current?.Context);

        if (OnPublishAsync is not null)
        {
            await OnPublishAsync(envelope, ct);
        }

        _publishedEnvelopes.Add(envelope);
    }

    public async Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        ct.ThrowIfCancellationRequested();

        _publishedBatches.Add([.. envelopes]);
        foreach (var envelope in envelopes)
        {
            await PublishAsync(envelope, ct);
        }
    }
}
