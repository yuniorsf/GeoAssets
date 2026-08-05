namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderTypeTransitions</c> table — see <see cref="Orders.WorkflowTransition"/>.</summary>
internal sealed class OrderTypeTransitionRecord
{
    public int    Id           { get; set; }
    public string OrderTypeId  { get; set; } = string.Empty;

    public string FromStateKey { get; set; } = string.Empty;
    public string ToStateKey   { get; set; } = string.Empty;

    /// <summary><see cref="Orders.OrderActionType"/>? stored as int, nullable.</summary>
    public int?   TriggerAction { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public OrderTypeRecord OrderType { get; set; } = null!;
}
