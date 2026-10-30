namespace GeoAssets.Core.Messaging;

/// <summary>
/// Immutable transport metadata supplied to <see cref="IIntegrationEventHandler{TEvent}"/>
/// alongside the deserialized integration event.
/// </summary>
public sealed record IntegrationMessageContext(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    string PartitionKey,
    string ContentType,
    string TransportName,
    string DestinationName,
    string ConsumerName,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string? CatalogInstanceId = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string? TraceParent = null,
    string? TraceState = null,
    IReadOnlyDictionary<string, string>? Headers = null);
