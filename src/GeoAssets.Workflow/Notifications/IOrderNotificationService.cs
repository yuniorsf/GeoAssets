namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// High-level service that orchestrates notifications when a service order
/// changes state.
///
/// Responsibilities:
///   1. Build an <see cref="OrderStateChangedEvent"/> from the domain objects.
///   2. Resolve the relevant actors (creator, assignees, dispatched groups, …).
///   3. Delegate to <see cref="IOrderEventPublisher"/> for actual delivery.
///
/// Decoupled from identity — caller passes recipient IDs as opaque strings.
/// </summary>
public interface IOrderNotificationService
{
    /// <summary>
    /// Notifies all relevant actors that <paramref name="orderId"/> has
    /// transitioned from <paramref name="previous"/> to <paramref name="newStatus"/>.
    /// </summary>
    /// <param name="orderId">Identifier of the affected order.</param>
    /// <param name="orderTypeId">Order type slug (e.g. "inspection").</param>
    /// <param name="previous">Status before the transition.</param>
    /// <param name="newStatus">Status after the transition.</param>
    /// <param name="performedBy">Actor (userId or system) who made the change.</param>
    /// <param name="recipientIds">
    /// Opaque list of actor IDs to notify (users, groups, etc.).
    /// Caller is responsible for resolving the correct actors beforehand.
    /// </param>
    /// <param name="correlationId">Optional trace / saga correlation token.</param>
    /// <param name="metadata">Additional domain-specific context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyStateChangedAsync(
        string                               orderId,
        string                               orderTypeId,
        Orders.ServiceOrderStatus            previous,
        Orders.ServiceOrderStatus            newStatus,
        string                               performedBy,
        IEnumerable<string>                  recipientIds,
        string?                              correlationId = null,
        IReadOnlyDictionary<string, string>? metadata     = null,
        CancellationToken                    ct            = default);
}
