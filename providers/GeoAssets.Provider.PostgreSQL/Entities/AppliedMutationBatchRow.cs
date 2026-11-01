namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>
/// EF Core entity that maps to the <c>applied_mutation_batches</c> table — backs
/// <see cref="GeoAssets.Core.Interfaces.IAppliedMutationBatchStore"/> (XD01-229). Stores the full
/// applied-items/result shape (not just the <see cref="BatchId"/>) so a redelivered batch can
/// return the exact original <c>AssetBatchAppliedEvent</c> rather than an approximation.
/// </summary>
public sealed class AppliedMutationBatchRow
{
    public Guid BatchId { get; set; }
    public string CatalogInstanceId { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public string RequestedByService { get; set; } = string.Empty;
    public string AppliedByUserId { get; set; } = string.Empty;
    public string? SourceOrderId { get; set; }
    public string? SourceOrderTypeId { get; set; }
    public string? ScopeId { get; set; }
    public string AppliedItemsJson { get; set; } = "[]";
    public DateTime AppliedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}
