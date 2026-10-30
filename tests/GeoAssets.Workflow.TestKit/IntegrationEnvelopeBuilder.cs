using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class IntegrationEnvelopeBuilder
{
    private Guid _messageId = IntegrationMessagingTestDataBuilders.DefaultMessageId;
    private string _messageType = IntegrationEventTypes.FeatureCatalogMutatedV1;
    private int _schemaVersion = IntegrationEventTypes.CurrentSchemaVersion;
    private string _partitionKey = $"{IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId}:feat-100";
    private string _contentType = "application/x-protobuf";
    private ReadOnlyMemory<byte> _payload = new byte[] { 0x0A, 0x04, 0x74, 0x65, 0x73, 0x74 };
    private DateTimeOffset _occurredAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private string? _sourceService = "geoassets-server";
    private string? _correlationId = "corr-100";
    private string? _causationId = "caus-099";
    private string? _traceParent;
    private string? _traceState;
    private IReadOnlyDictionary<string, string>? _headers;

    public IntegrationEnvelopeBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    public IntegrationEnvelopeBuilder WithMessageType(string messageType)
    {
        _messageType = messageType;
        return this;
    }

    public IntegrationEnvelopeBuilder WithSchemaVersion(int schemaVersion)
    {
        _schemaVersion = schemaVersion;
        return this;
    }

    public IntegrationEnvelopeBuilder WithPartitionKey(string partitionKey)
    {
        _partitionKey = partitionKey;
        return this;
    }

    public IntegrationEnvelopeBuilder WithContentType(string contentType)
    {
        _contentType = contentType;
        return this;
    }

    public IntegrationEnvelopeBuilder WithPayload(ReadOnlyMemory<byte> payload)
    {
        _payload = payload;
        return this;
    }

    public IntegrationEnvelopeBuilder WithOccurredAtUtc(DateTimeOffset occurredAtUtc)
    {
        _occurredAtUtc = occurredAtUtc;
        return this;
    }

    public IntegrationEnvelopeBuilder WithCatalogInstanceId(string? catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public IntegrationEnvelopeBuilder WithSourceService(string? sourceService)
    {
        _sourceService = sourceService;
        return this;
    }

    public IntegrationEnvelopeBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public IntegrationEnvelopeBuilder WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    public IntegrationEnvelopeBuilder WithTraceParent(string? traceParent)
    {
        _traceParent = traceParent;
        return this;
    }

    public IntegrationEnvelopeBuilder WithTraceState(string? traceState)
    {
        _traceState = traceState;
        return this;
    }

    public IntegrationEnvelopeBuilder WithHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        _headers = headers;
        return this;
    }

    public IntegrationEnvelope Build() =>
        new(
            MessageId: _messageId,
            MessageType: _messageType,
            SchemaVersion: _schemaVersion,
            PartitionKey: _partitionKey,
            ContentType: _contentType,
            Payload: _payload,
            OccurredAtUtc: _occurredAtUtc,
            CatalogInstanceId: _catalogInstanceId,
            SourceService: _sourceService,
            CorrelationId: _correlationId,
            CausationId: _causationId,
            TraceParent: _traceParent,
            TraceState: _traceState,
            Headers: _headers);
}
