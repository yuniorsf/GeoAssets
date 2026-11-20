using System.Diagnostics;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// One consumer of the in-process integration transport (XD01-383): the <see cref="IIntegrationEventHandler{TEvent}"/>
/// that <see cref="InProcessIntegrationEventPublisher"/> hands every envelope of <see cref="MessageType"/> to, with the
/// same inbox deduplication and trace continuation a broker consumer applies.
/// </summary>
public abstract class InProcessIntegrationRoute
{
    /// <summary>How long a processed message stays in the inbox for deduplication, as for the broker consumers.</summary>
    public static readonly TimeSpan InboxRetention = TimeSpan.FromDays(7);

    /// <summary>The transport name the handlers see in <see cref="IntegrationMessageContext.TransportName"/>.</summary>
    public const string TransportName = "in-process";

    protected InProcessIntegrationRoute(string messageType, string consumerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);

        MessageType = messageType;
        ConsumerName = consumerName;
    }

    /// <summary>The message type this route delivers (ordinal).</summary>
    public string MessageType { get; }

    /// <summary>The inbox consumer name the delivery is deduplicated under.</summary>
    public string ConsumerName { get; }

    /// <summary>
    /// Delivers <paramref name="envelope"/> in a scope of its own: skipped when the inbox already holds it for
    /// <see cref="ConsumerName"/>, otherwise deserialized, handled and recorded — the broker consumers' protocol. A failure
    /// propagates, so the relay marks the message failed and retries it.
    /// </summary>
    public abstract Task DeliverAsync(IntegrationEnvelope envelope, IServiceProvider services, CancellationToken ct);
}

/// <summary>The <see cref="InProcessIntegrationRoute"/> of <typeparamref name="TEvent"/>'s handler.</summary>
public sealed class InProcessIntegrationRoute<TEvent> : InProcessIntegrationRoute
    where TEvent : notnull
{
    public InProcessIntegrationRoute(string messageType, string consumerName)
        : base(messageType, consumerName)
    {
    }

    public override async Task DeliverAsync(IntegrationEnvelope envelope, IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(services);

        var tracer = services.GetRequiredService<GeoAssetsActivitySource>();
        var serializer = services.GetRequiredService<IIntegrationMessageSerializer>();
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;

        // The consume span continues the trace the envelope was staged in — as a child of the relay's span, which is that
        // trace's, when it is current; of the envelope's traceparent otherwise — so whatever the handler stages carries it on.
        var parent = Activity.Current is null
            && !string.IsNullOrWhiteSpace(envelope.TraceParent)
            && ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: false, out var parsed)
            ? parsed
            : default;
        using var activity = tracer.StartIntegrationConsumeActivity(
            envelope.MessageType, envelope.MessageId, TransportName, TransportName, ConsumerName, envelope.CatalogInstanceId, parent);

        try
        {
            var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IIdempotentInbox>();
                if (await inbox.HasProcessedAsync(envelope.MessageId, ConsumerName, ct).ConfigureAwait(false))
                {
                    activity?.SetTag("messaging.idempotent.duplicate", true);
                    return;
                }

                var message = serializer.Deserialize<TEvent>(envelope.Payload);
                var context = ContextFor(envelope, timeProvider.GetUtcNow());

                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
                await handler.HandleAsync(message, context, ct).ConfigureAwait(false);

                if (!await inbox.TryRecordProcessedAsync(envelope.MessageId, ConsumerName, envelope.MessageType, InboxRetention, ct).ConfigureAwait(false))
                    activity?.SetTag("messaging.idempotent.duplicate", true);
            }
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            throw;
        }
    }

    private IntegrationMessageContext ContextFor(IntegrationEnvelope envelope, DateTimeOffset receivedAt) => new(
        MessageId: envelope.MessageId,
        MessageType: envelope.MessageType,
        SchemaVersion: envelope.SchemaVersion,
        PartitionKey: envelope.PartitionKey,
        ContentType: envelope.ContentType,
        TransportName: TransportName,
        DestinationName: TransportName,
        ConsumerName: ConsumerName,
        OccurredAtUtc: envelope.OccurredAtUtc,
        ReceivedAtUtc: receivedAt,
        CatalogInstanceId: envelope.CatalogInstanceId,
        CorrelationId: envelope.CorrelationId,
        CausationId: envelope.CausationId,
        TraceParent: envelope.TraceParent,
        TraceState: envelope.TraceState,
        Headers: envelope.Headers);
}
