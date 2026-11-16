namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for the <c>OrderTypes</c> table.</summary>
internal sealed class OrderTypeRecord
{
    public string  Id          { get; set; } = string.Empty;
    public string  DisplayName { get; set; } = string.Empty;
    public string  Description { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<OrderCreationPolicyRecord>  CreationPolicies  { get; set; } = [];
    public List<OrderActionPermissionRecord> ActionPermissions { get; set; } = [];
    public List<OrderTypeStateRecord>       States            { get; set; } = [];
    public List<OrderTypeTransitionRecord>  Transitions       { get; set; } = [];

    public string? InitialStateKey { get; set; }

    /// <summary>OrderType.AttributesSchemaJson, stored verbatim. Null means unrestricted.</summary>
    public string? AttributesSchemaJson { get; set; }

    /// <summary>
    /// OrderType.TargetPolicy serialized by <see cref="OrderTargetPolicyMapper"/>. Null means
    /// the order type has no target policy.
    /// </summary>
    public string? TargetPolicyJson { get; set; }
}
