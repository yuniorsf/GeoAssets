using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.TestKit;

public sealed class IntegrationMessageContextBuilder
{
    private Guid _messageId = IntegrationMessagingTestDataBuilders.DefaultMessageId;
    private string _messageType = IntegrationEventTypes.FeatureCatalogMutatedV1;
    private int _schemaVersion = IntegrationEventTypes.CurrentSchemaVersion;
    private string _partitionKey = $"{IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId}:feat-100";
    private string _contentType = "application/x-protobuf";
    private string _transportName = "kafka";
    private string _destinationName = "geoassets.catalog.events";
    private string _consumerName = "test-consumer";
    private DateTimeOffset _occurredAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private DateTimeOffset _receivedAtUtc = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc.AddSeconds(2);
    private string? _catalogInstanceId = IntegrationMessagingTestDataBuilders.DefaultCatalogInstanceId;
    private string? _correlationId = "corr-100";
    private string? _causationId = "caus-099";
    private string? _traceParent;
    private string? _traceState;
    private IReadOnlyDictionary<string, string>? _headers;

    public IntegrationMessageContextBuilder WithMessageId(Guid messageId)
    {
        _messageId = messageId;
        return this;
    }

    public IntegrationMessageContextBuilder WithMessageType(string messageType)
    {
        _messageType = messageType;
        return this;
    }

    public IntegrationMessageContextBuilder WithSchemaVersion(int schemaVersion)
    {
        _schemaVersion = schemaVersion;
        return this;
    }

    public IntegrationMessageContextBuilder WithPartitionKey(string partitionKey)
    {
        _partitionKey = partitionKey;
        return this;
    }

    public IntegrationMessageContextBuilder WithContentType(string contentType)
    {
        _contentType = contentType;
        return this;
    }

    public IntegrationMessageContextBuilder WithTransportName(string transportName)
    {
        _transportName = transportName;
        return this;
    }

    public IntegrationMessageContextBuilder WithDestinationName(string destinationName)
    {
        _destinationName = destinationName;
        return this;
    }

    public IntegrationMessageContextBuilder WithConsumerName(string consumerName)
    {
        _consumerName = consumerName;
        return this;
    }

    public IntegrationMessageContextBuilder WithOccurredAtUtc(DateTimeOffset occurredAtUtc)
    {
        _occurredAtUtc = occurredAtUtc;
        return this;
    }

    public IntegrationMessageContextBuilder WithReceivedAtUtc(DateTimeOffset receivedAtUtc)
    {
        _receivedAtUtc = receivedAtUtc;
        return this;
    }

    public IntegrationMessageContextBuilder WithCatalogInstanceId(string? catalogInstanceId)
    {
        _catalogInstanceId = catalogInstanceId;
        return this;
    }

    public IntegrationMessageContextBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public IntegrationMessageContextBuilder WithCausationId(string? causationId)
    {
        _causationId = causationId;
        return this;
    }

    public IntegrationMessageContextBuilder WithTraceParent(string? traceParent)
    {
        _traceParent = traceParent;
        return this;
    }

    public IntegrationMessageContextBuilder WithTraceState(string? traceState)
    {
        _traceState = traceState;
        return this;
    }

    public IntegrationMessageContextBuilder WithHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        _headers = headers;
        return this;
    }

    public IntegrationMessageContext Build() =>
        new(
            MessageId: _messageId,
            MessageType: _messageType,
            SchemaVersion: _schemaVersion,
            PartitionKey: _partitionKey,
            ContentType: _contentType,
            TransportName: _transportName,
            DestinationName: _destinationName,
            ConsumerName: _consumerName,
            OccurredAtUtc: _occurredAtUtc,
            ReceivedAtUtc: _receivedAtUtc,
            CatalogInstanceId: _catalogInstanceId,
            CorrelationId: _correlationId,
            CausationId: _causationId,
            TraceParent: _traceParent,
            TraceState: _traceState,
            Headers: _headers);
}
