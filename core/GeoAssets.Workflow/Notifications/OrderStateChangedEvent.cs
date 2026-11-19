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
/// <param name="VerticalId">
/// The vertical that owns the order (XD01-379); null — what every publisher before the global target ledger sends —
/// means an in-process order of the kernel (<see cref="Targets.TargetLedger.KernelVerticalId"/>).
/// </param>
/// <param name="IsTerminal">
/// Whether <paramref name="NewStatus"/> is a terminal state of the order's type, as the publishing vertical's own state
/// machine decides it; null when the publisher does not say (the kernel then falls back to the terminal statuses the
/// vertical published for the order type).
/// </param>
/// <param name="OrganizationId">
/// The organization that owns the order (XD01-379); a remote vertical sets it so the kernel's terminal release runs
/// scoped to that organization (row-level security). Null — every publisher before the global target ledger — leaves
/// the release unscoped, which only finds the order's targets where the database role is not subject to row-level security.
/// </param>
public sealed record OrderStateChangedEvent(
    string OrderId,
    string OrderTypeId,
    string PreviousStatus,
    string NewStatus,
    string PerformedBy,
    DateTimeOffset OccurredAt,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    string? VerticalId = null,
    bool? IsTerminal = null,
    Guid? OrganizationId = null)
{
    /// <summary>Creates an event with <see cref="OccurredAt"/> set to now.</summary>
    public static OrderStateChangedEvent Create(
        string orderId,
        string orderTypeId,
        string previous,
        string next,
        string performedBy,
        TimeProvider timeProvider,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new(orderId, orderTypeId, previous, next, performedBy,
               timeProvider.GetUtcNow(), correlationId, metadata);
    }
}
