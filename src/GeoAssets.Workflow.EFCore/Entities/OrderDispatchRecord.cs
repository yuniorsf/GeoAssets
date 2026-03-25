using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>EF entity for a single dispatch event — maps to the <c>OrderDispatches</c> table.</summary>
internal sealed class OrderDispatchRecord
{
    public int    Id             { get; set; }
    public string ServiceOrderId { get; set; } = string.Empty;
    public string TargetId       { get; set; } = string.Empty;
    public int    TargetType     { get; set; }   // DispatchTargetType enum stored as int
    public string DispatchedBy   { get; set; } = string.Empty;
    public DateTime DispatchedAt { get; set; }
    public string?  Note         { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public ServiceOrderRecord ServiceOrder { get; set; } = null!;
}
