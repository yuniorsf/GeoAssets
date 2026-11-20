using GeoAssets.Core.Messaging;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The outbox relay's "broker" on a host with none (XD01-383, replacing XD01-337's in-process asset-mutation publisher):
/// every envelope is handed, in process, to each <see cref="InProcessIntegrationRoute"/> registered for its message type —
/// the same handlers, with the same inbox deduplication, that a broker consumer would run. The relay's lease, exponential
/// backoff and attempt limit apply unchanged: a delivery that fails is marked failed and retried later, and a route that
/// already handled the message skips it on the retry. The relay of such a host claims only the routed message types
/// (<see cref="IntegrationConsumerHostExtensions"/>), so an envelope of any other type never reaches this and is refused.
/// </summary>
public sealed class InProcessIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IReadOnlyList<InProcessIntegrationRoute> _routes;
    private readonly IServiceProvider _services;

    public InProcessIntegrationEventPublisher(IEnumerable<InProcessIntegrationRoute> routes, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(services);

        _routes = [.. routes];
        _services = services;
    }

    /// <summary>The message types this transport delivers.</summary>
    public IReadOnlyList<string> MessageTypes => [.. _routes.Select(r => r.MessageType).Distinct(StringComparer.Ordinal)];

    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var routes = _routes.Where(r => string.Equals(r.MessageType, envelope.MessageType, StringComparison.Ordinal)).ToList();
        if (routes.Count == 0)
        {
            throw new NotSupportedException(
                $"{nameof(InProcessIntegrationEventPublisher)} has no in-process consumer for '{envelope.MessageType}' messages.");
        }

        foreach (var route in routes)
            await route.DeliverAsync(envelope, _services, ct).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        foreach (var envelope in envelopes)
            await PublishAsync(envelope, ct).ConfigureAwait(false);
    }
}
