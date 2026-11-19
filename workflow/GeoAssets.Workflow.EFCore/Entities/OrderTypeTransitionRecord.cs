namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderTypeTransitions</c> table — see <see cref="Orders.WorkflowTransition"/>.</summary>
internal sealed class OrderTypeTransitionRecord
{
    public int    Id           { get; set; }
    public string OrderTypeId  { get; set; } = string.Empty;

    public string FromStateKey { get; set; } = string.Empty;
    public string ToStateKey   { get; set; } = string.Empty;

    /// <summary>The trigger action's historical number; null when there is no trigger or it has no number (XD01-366).</summary>
    public int?   TriggerAction { get; set; }

    /// <summary>The trigger action's code (XD01-366); null when there is no trigger, or on a row written before the column existed.</summary>
    public string? TriggerActionCode { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public OrderTypeRecord OrderType { get; set; } = null!;
}
