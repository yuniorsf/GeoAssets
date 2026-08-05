namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderTypeStates</c> table — see <see cref="Orders.WorkflowState"/>.</summary>
internal sealed class OrderTypeStateRecord
{
    public int    Id          { get; set; }
    public string OrderTypeId { get; set; } = string.Empty;

    public string Key         { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool   IsSuccess   { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public OrderTypeRecord OrderType { get; set; } = null!;
}
