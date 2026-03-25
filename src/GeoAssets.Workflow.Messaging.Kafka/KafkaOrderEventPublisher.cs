using System.Text.Json;
using Confluent.Kafka;
using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Publishes <see cref="OrderStateChangedEvent"/> objects to an Apache Kafka topic
/// using the official <c>Confluent.Kafka</c> producer.
///
/// Message layout:
/// <list type="bullet">
///   <item><term>Key</term><description><see cref="OrderStateChangedEvent.OrderId"/> (when <see cref="KafkaPublisherOptions.UseOrderIdAsKey"/> is true) — guarantees ordering per order on a single partition.</description></item>
///   <item><term>Value</term><description>JSON-encoded UTF-8 payload.</description></item>
///   <item><term>Header "orderTypeId"</term><description>For consumer-side routing.</description></item>
///   <item><term>Header "newStatus"</term><description>For consumer-side filtering.</description></item>
///   <item><term>Header "correlationId"</term><description>Forwarded when present.</description></item>
/// </list>
/// </summary>
public sealed class KafkaOrderEventPublisher : IOrderEventPublisher, IDisposable
{
    private readonly IProducer<string?, string> _producer;
    private readonly KafkaPublisherOptions _options;
    private readonly ILogger<KafkaOrderEventPublisher> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public KafkaOrderEventPublisher(
        IOptions<KafkaPublisherOptions> options,
        ILogger<KafkaOrderEventPublisher> logger)
    {
        _options = options.Value;
        _logger  = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId         = _options.ClientId,
        };

        foreach (var (k, v) in _options.AdditionalConfig)
            config.Set(k, v);

        _producer = new ProducerBuilder<string?, string>(config).Build();
    }

    public async Task PublishAsync(OrderStateChangedEvent evt, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(evt, _jsonOptions);
        var key     = _options.UseOrderIdAsKey ? evt.OrderId : null;

        var message = new Message<string?, string>
        {
            Key     = key,
            Value   = payload,
            Headers = BuildHeaders(evt),
        };

        try
        {
            var result = await _producer
                .ProduceAsync(_options.TopicName, message, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Published OrderStateChangedEvent for order {OrderId} ({Previous} → {New}) " +
                "to Kafka topic '{Topic}' partition {Partition} offset {Offset}.",
                evt.OrderId, evt.PreviousStatus, evt.NewStatus,
                _options.TopicName, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string?, string> ex)
        {
            _logger.LogError(ex,
                "Failed to publish OrderStateChangedEvent for order {OrderId} to Kafka. " +
                "Error: {KafkaError}",
                evt.OrderId, ex.Error.Reason);
            throw;
        }
    }

    private static Headers BuildHeaders(OrderStateChangedEvent evt)
    {
        var headers = new Headers
        {
            { "orderTypeId", System.Text.Encoding.UTF8.GetBytes(evt.OrderTypeId) },
            { "newStatus",   System.Text.Encoding.UTF8.GetBytes(evt.NewStatus.ToString()) },
        };

        if (evt.CorrelationId is not null)
            headers.Add("correlationId", System.Text.Encoding.UTF8.GetBytes(evt.CorrelationId));

        return headers;
    }

    public void Dispose()
    {
        // Flush any buffered messages before disposing.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
