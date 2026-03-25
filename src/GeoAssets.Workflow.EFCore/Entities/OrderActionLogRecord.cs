namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for an audit log entry — maps to the <c>OrderActionLogs</c> table.</summary>
internal sealed class OrderActionLogRecord
{
    public int    Id             { get; set; }
    public string ServiceOrderId { get; set; } = string.Empty;
    public int    Action         { get; set; }   // OrderActionType enum stored as int
    public string PerformedBy    { get; set; } = string.Empty;
    public DateTime PerformedAt  { get; set; }
    public string?  Comment      { get; set; }
    public int?     ResultingStatus { get; set; }   // ServiceOrderStatus? stored as int

    // ── Navigation ────────────────────────────────────────────────────────────

    public ServiceOrderRecord ServiceOrder { get; set; } = null!;
}
