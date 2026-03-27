namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity that maps to the <c>ServiceOrders</c> table.
/// Kept separate from the domain <see cref="Orders.ServiceOrder"/> so the domain
/// model stays free of EF annotations and infrastructure concerns.
///
/// Complex collections (<see cref="Dispatches"/>, <see cref="ActionLog"/>) use
/// separate tables so they can be queried independently.
/// Scalar complex values (<see cref="AttributesJson"/>, <see cref="FeatureIdsJson"/>,
/// <see cref="SelectionSpecJson"/>) are stored as JSON text.
/// </summary>
internal sealed class ServiceOrderRecord
{
    public string Id          { get; set; } = string.Empty;
    public string Title       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OrderTypeId { get; set; } = string.Empty;

    public int Status   { get; set; }
    public int Priority { get; set; }

    public string  CreatedBy  { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }

    public DateTime  CreatedAt   { get; set; }
    public DateTime? UpdatedAt   { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>FK to the parent order, or null for root orders.</summary>
    public string? ParentOrderId { get; set; }

    /// <summary>JSON-serialised <c>Dictionary&lt;string,string&gt;</c>.</summary>
    public string AttributesJson   { get; set; } = "{}";

    /// <summary>JSON-serialised <c>string[]</c> of <see cref="GeoAssets.Core.Models.GeoFeature"/> IDs.</summary>
    public string FeatureIdsJson   { get; set; } = "[]";

    /// <summary>JSON-serialised <see cref="Selection.FeatureSelectionSpec"/>, or null.</summary>
    public string? SelectionSpecJson { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public List<OrderDispatchRecord>   Dispatches { get; set; } = [];
    public List<OrderActionLogRecord>  ActionLog  { get; set; } = [];
}
