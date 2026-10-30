namespace GeoAssets.Core.Messaging;

/// <summary>
/// Transport- and wire-format-agnostic envelope carrying an encoded integration payload
/// alongside routing, schema, shard-fencing, and W3C distributed trace metadata.
/// </summary>
public sealed record IntegrationEnvelope(
    Guid MessageId,
    string MessageType,
    int SchemaVersion,
    string PartitionKey,
    string ContentType,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset OccurredAtUtc,
    string? CatalogInstanceId = null,
    string? SourceService = null,
    string? CorrelationId = null,
    string? CausationId = null,
    string? TraceParent = null,
    string? TraceState = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public static IntegrationEnvelope Create(
        string messageType,
        int schemaVersion,
        string partitionKey,
        string contentType,
        ReadOnlyMemory<byte> payload,
        TimeProvider timeProvider,
        Guid? messageId = null,
        string? catalogInstanceId = null,
        string? sourceService = null,
        string? correlationId = null,
        string? causationId = null,
        string? traceParent = null,
        string? traceState = null,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var resolvedId = messageId ?? Guid.NewGuid();
        if (resolvedId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be Guid.Empty.", nameof(messageId));
        }

        return new IntegrationEnvelope(
            MessageId: resolvedId,
            MessageType: messageType,
            SchemaVersion: schemaVersion,
            PartitionKey: partitionKey,
            ContentType: contentType,
            Payload: payload,
            OccurredAtUtc: timeProvider.GetUtcNow(),
            CatalogInstanceId: catalogInstanceId,
            SourceService: sourceService,
            CorrelationId: correlationId,
            CausationId: causationId,
            TraceParent: traceParent,
            TraceState: traceState,
            Headers: headers);
    }
}
