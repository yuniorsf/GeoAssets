namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>field_contractor_us_postings</c> table (XD01-242). Kept
/// separate from the domain <see cref="GeoAssets.Workflow.Contractors.ContractorUsPosting"/> so
/// the domain model stays free of EF annotations — same split as
/// <see cref="ContractorUsLedgerEntryRecord"/>. <see cref="TotalUs"/>/<see cref="TotalBrl"/> are
/// persisted (not recomputed on read) — a faithful snapshot of the figures actually posted,
/// rather than values that could technically drift if this record's other columns were ever
/// hand-edited outside the (nonexistent) update path.
/// </summary>
internal sealed class ContractorUsPostingRecord
{
    public Guid Id { get; set; } = Guid.Empty;
    public string ServiceOrderId { get; set; } = string.Empty;
    public Guid ContractorOrganizationId { get; set; } = Guid.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitUs { get; set; }
    public decimal TotalUs { get; set; }
    public decimal MonetaryRate { get; set; }
    public decimal TotalBrl { get; set; }
    public DateTime PostedAtUtc { get; set; }
}
