namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>UsCatalogItems</c> table. Kept separate from the domain
/// <see cref="GeoAssets.Workflow.Contractors.UsCatalogItem"/> so the domain model stays free of
/// EF annotations — same split as <see cref="ContractorUsLedgerEntryRecord"/>.
/// </summary>
internal sealed class UsCatalogItemRecord
{
    public Guid Id { get; set; } = Guid.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;

    public string ItemCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal BaseUsFactor { get; set; }
    public decimal RegionalMultiplier { get; set; } = 1m;
    public decimal MonetaryConversionRate { get; set; }

    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}
