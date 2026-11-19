namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderActionPermissions</c> table.</summary>
internal sealed class OrderActionPermissionRecord
{
    public int    Id          { get; set; }
    public string OrderTypeId { get; set; } = string.Empty;

    /// <summary>The action's historical number (XD01-366: null for an action that has none — see <c>StoredOrderAction</c>).</summary>
    public int?   Action { get; set; }

    /// <summary>The action's code (XD01-366); null only on a row written before the column existed.</summary>
    public string? ActionCode { get; set; }

    /// <summary><see cref="Orders.PolicyKind"/> stored as int.</summary>
    public int    Kind  { get; set; }
    public string Value { get; set; } = string.Empty;

    /// <summary>See <see cref="Orders.OrderActionPermission.FromStateKey"/>. Null = applies regardless of state.</summary>
    public string? FromStateKey { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public OrderTypeRecord OrderType { get; set; } = null!;
}
