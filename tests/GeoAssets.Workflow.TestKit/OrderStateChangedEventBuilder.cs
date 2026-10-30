using GeoAssets.Workflow.Notifications;

namespace GeoAssets.Workflow.TestKit;

public sealed class OrderStateChangedEventBuilder
{
    private string _orderId = "ord-100";
    private string _orderTypeId = "inspection";
    private string _previousStatus = "Pending";
    private string _newStatus = "InProgress";
    private string _performedBy = "operator-1";
    private DateTimeOffset _occurredAt = IntegrationMessagingTestDataBuilders.DefaultOccurredAtUtc;
    private string? _correlationId = "corr-ord-100";
    private IReadOnlyDictionary<string, string>? _metadata = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["priority"] = "high",
        ["region"] = "north"
    };

    public OrderStateChangedEventBuilder WithOrderId(string orderId)
    {
        _orderId = orderId;
        return this;
    }

    public OrderStateChangedEventBuilder WithOrderTypeId(string orderTypeId)
    {
        _orderTypeId = orderTypeId;
        return this;
    }

    public OrderStateChangedEventBuilder WithPreviousStatus(string previousStatus)
    {
        _previousStatus = previousStatus;
        return this;
    }

    public OrderStateChangedEventBuilder WithNewStatus(string newStatus)
    {
        _newStatus = newStatus;
        return this;
    }

    public OrderStateChangedEventBuilder WithPerformedBy(string performedBy)
    {
        _performedBy = performedBy;
        return this;
    }

    public OrderStateChangedEventBuilder WithOccurredAt(DateTimeOffset occurredAt)
    {
        _occurredAt = occurredAt;
        return this;
    }

    public OrderStateChangedEventBuilder WithCorrelationId(string? correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public OrderStateChangedEventBuilder WithMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        _metadata = metadata;
        return this;
    }

    public OrderStateChangedEvent Build() =>
        new(
            OrderId: _orderId,
            OrderTypeId: _orderTypeId,
            PreviousStatus: _previousStatus,
            NewStatus: _newStatus,
            PerformedBy: _performedBy,
            OccurredAt: _occurredAt,
            CorrelationId: _correlationId,
            Metadata: _metadata);
}
