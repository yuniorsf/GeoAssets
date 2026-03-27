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
}
