using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Publishes <see cref="OrderStateChangedEvent"/> objects to an Azure Service Bus
/// topic or queue using the official <c>Azure.Messaging.ServiceBus</c> SDK.
///
/// The payload is JSON-encoded UTF-8.  Message properties:
/// <list type="bullet">
///   <item><term>Subject</term><description><see cref="ServiceBusPublisherOptions.MessageSubject"/></description></item>
///   <item><term>MessageId</term><description>Deterministic: <c>{orderId}:{newStatus}:{occurredAt.Ticks}</c></description></item>
///   <item><term>CorrelationId</term><description>Forwarded from <see cref="OrderStateChangedEvent.CorrelationId"/>.</description></item>
///   <item><term>ApplicationProperties["orderId"]</term><description>For Service Bus topic filter subscriptions.</description></item>
///   <item><term>ApplicationProperties["orderTypeId"]</term><description>For Service Bus topic filter subscriptions.</description></item>
///   <item><term>ApplicationProperties["newStatus"]</term><description>For Service Bus topic filter subscriptions.</description></item>
///   <item><term>ApplicationProperties["traceparent"]</term><description>W3C trace context of the publishing span, so a future consumer can continue the same trace instead of starting a disconnected one.</description></item>
/// </list>
/// </summary>
public sealed class ServiceBusOrderEventPublisher : IOrderEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ServiceBusPublisherOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<ServiceBusOrderEventPublisher> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        WriteIndented               = false,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ServiceBusOrderEventPublisher(
        IOptions<ServiceBusPublisherOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<ServiceBusOrderEventPublisher> logger)
    {
        _options = options.Value;
        _tracer  = tracer;
        _logger  = logger;

        var client = new ServiceBusClient(_options.ConnectionString);
        _sender    = client.CreateSender(_options.TopicName);
    }

    public async Task PublishAsync(OrderStateChangedEvent evt, CancellationToken ct = default)
    {
        using var activity = _tracer.StartNotificationActivity(evt.OrderId, "servicebus", ActivityKind.Producer);

        var json    = JsonSerializer.Serialize(evt, _jsonOptions);
        var body    = new BinaryData(Encoding.UTF8.GetBytes(json));

        var message = new ServiceBusMessage(body)
        {
            Subject       = _options.MessageSubject,
            MessageId     = $"{evt.OrderId}:{evt.NewStatus}:{evt.OccurredAt.Ticks}",
            ContentType   = "application/json",
            CorrelationId = evt.CorrelationId,
        };

        message.ApplicationProperties["orderId"]     = evt.OrderId;
        message.ApplicationProperties["orderTypeId"] = evt.OrderTypeId;
        message.ApplicationProperties["newStatus"]   = evt.NewStatus.ToString();
        ApplyTraceContext(message.ApplicationProperties, activity);

        try
        {
            await _sender.SendMessageAsync(message, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Published OrderStateChangedEvent for order {OrderId} ({Previous} → {New}) to Service Bus topic '{Topic}'.",
                evt.OrderId, evt.PreviousStatus, evt.NewStatus, _options.TopicName);
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            _logger.LogError(ex,
                "Failed to publish OrderStateChangedEvent for order {OrderId} to Service Bus.",
                evt.OrderId);
            throw;
        }
    }

    /// <summary>
    /// Injects the W3C <c>traceparent</c> (and <c>tracestate</c>, if set) derived
    /// from <paramref name="activity"/> into <paramref name="applicationProperties"/>
    /// via <see cref="DistributedContextPropagator"/> — additive alongside the
    /// existing application properties, not a replacement for any of them.
    /// </summary>
    internal static void ApplyTraceContext(IDictionary<string, object> applicationProperties, Activity? activity) =>
        DistributedContextPropagator.Current.Inject(activity, applicationProperties,
            static (carrier, fieldName, fieldValue) =>
                ((IDictionary<string, object>)carrier!)[fieldName] = fieldValue);

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
    }
}
