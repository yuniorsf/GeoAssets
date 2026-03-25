namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderActionPermissions</c> table.</summary>
internal sealed class OrderActionPermissionRecord
{
    public int    Id          { get; set; }
    public string OrderTypeId { get; set; } = string.Empty;

    /// <summary><see cref="Orders.OrderActionType"/> stored as int.</summary>
    public int    Action { get; set; }

    /// <summary><see cref="Orders.PolicyKind"/> stored as int.</summary>
    public int    Kind  { get; set; }
    public string Value { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    public OrderTypeRecord OrderType { get; set; } = null!;
}
