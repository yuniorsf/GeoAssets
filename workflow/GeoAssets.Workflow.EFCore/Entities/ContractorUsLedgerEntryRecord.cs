namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>ContractorUsLedgerEntries</c> table. Kept separate from
/// the domain <see cref="GeoAssets.Workflow.Contractors.ContractorUsLedgerEntry"/> so the
/// domain model stays free of EF annotations. Photo proof is stored as JSON text, the same
/// convention as <see cref="ServiceOrderRecord.AttributesJson"/>/<see cref="ServiceOrderRecord.FeatureIdsJson"/>.
/// </summary>
internal sealed class ContractorUsLedgerEntryRecord
{
    public Guid Id { get; set; } = Guid.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;

    /// <summary>FK-like reference to <c>ServiceOrderRecord.Id</c>, or null if unlinked. No navigation/FK constraint — ServiceOrders can be deleted independently of ledger history.</summary>
    public string? ServiceOrderId { get; set; }

    public string UsCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitValue { get; set; }

    /// <summary>JSON-serialised array of photo proof (URL + longitude/latitude + capture time).</summary>
    public string PhotoProofJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
