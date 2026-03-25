namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// Describes a service order state transition, published whenever
/// <see cref="Orders.ServiceOrderStatus"/> changes.
/// </summary>
/// <param name="OrderId">Unique identifier of the affected order.</param>
/// <param name="OrderTypeId">Type slug (e.g. "inspection").</param>
/// <param name="PreviousStatus">Status before the transition.</param>
/// <param name="NewStatus">Status after the transition.</param>
/// <param name="PerformedBy">UserId or system actor that triggered the change.</param>
/// <param name="OccurredAt">UTC instant of the transition.</param>
/// <param name="CorrelationId">Optional trace / saga correlation token.</param>
/// <param name="Metadata">Arbitrary key/value pairs for domain-specific context.</param>
public sealed record OrderStateChangedEvent(
    string                              OrderId,
    string                              OrderTypeId,
    Orders.ServiceOrderStatus           PreviousStatus,
    Orders.ServiceOrderStatus           NewStatus,
    string                              PerformedBy,
    DateTimeOffset                      OccurredAt,
    string?                             CorrelationId = null,
    IReadOnlyDictionary<string, string>? Metadata     = null)
{
    /// <summary>Creates an event with <see cref="OccurredAt"/> set to now.</summary>
    public static OrderStateChangedEvent Create(
        string                              orderId,
        string                              orderTypeId,
        Orders.ServiceOrderStatus           previous,
        Orders.ServiceOrderStatus           next,
        string                              performedBy,
        string?                             correlationId = null,
        IReadOnlyDictionary<string, string>? metadata     = null)
        => new(orderId, orderTypeId, previous, next, performedBy,
               DateTimeOffset.UtcNow, correlationId, metadata);
}
