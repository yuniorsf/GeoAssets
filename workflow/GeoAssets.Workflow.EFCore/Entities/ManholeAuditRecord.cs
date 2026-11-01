namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>field_manhole_audits</c> table (XD01-265). Kept separate
/// from the domain <see cref="GeoAssets.Workflow.Infrastructure.ManholeAudit"/> so the domain
/// model stays free of EF annotations — same split as <see cref="ContractorUsPostingRecord"/>.
/// <see cref="SubDuctsJson"/>/<see cref="Nr33ChecklistAttributesJson"/> are JSON text, same
/// convention as <see cref="ServiceOrderRecord.AttributesJson"/>.
/// </summary>
internal sealed class ManholeAuditRecord
{
    public Guid Id { get; set; } = Guid.Empty;
    public string ConduitFeatureId { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; } = Guid.Empty;
    public double BoreOccupancyPercentage { get; set; }
    public string SubDuctsJson { get; set; } = "[]";
    public string SpliceEnclosureCondition { get; set; } = string.Empty;
    public string Nr33ChecklistAttributesJson { get; set; } = "{}";
    public string AuditedBy { get; set; } = string.Empty;
    public DateTime AuditedAtUtc { get; set; }
}
