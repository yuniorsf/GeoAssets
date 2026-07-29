namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Immutable audit record of an action performed on a service order.
/// </summary>
public sealed record OrderActionLog(
    OrderActionType Action,
    string          PerformedBy,
    DateTime        PerformedAt,
    string?         Comment    = null,
    ServiceOrderStatus? ResultingStatus = null
)
{
    /// <summary>Whether a human, an AI agent, or an automated system performed this action.</summary>
    public ActorKind ActorKind { get; init; } = ActorKind.Human;

    /// <summary>
    /// Correlates this entry back to the agent run/thread that produced it.
    /// Only meaningful when <see cref="ActorKind"/> is <see cref="ActorKind.Agent"/>.
    /// </summary>
    public string? AgentInvocationId { get; init; }
}
