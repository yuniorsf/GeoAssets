using System.Text.Json;
using GeoAssets.Workflow.Infrastructure;
using GeoAssets.Workflow.Persistence.Entities;

namespace GeoAssets.Workflow.Persistence;

/// <summary>Converts between the domain <see cref="ManholeAudit"/> and the EF entity <see cref="ManholeAuditRecord"/>.</summary>
internal static class ManholeAuditMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ManholeAuditRecord ToRecord(ManholeAudit audit) => new()
    {
        Id = audit.Id,
        ConduitFeatureId = audit.ConduitFeatureId,
        OrganizationId = audit.OrganizationId,
        BoreOccupancyPercentage = audit.BoreOccupancyPercentage,
        SubDuctsJson = JsonSerializer.Serialize(audit.SubDucts, JsonOptions),
        SpliceEnclosureCondition = audit.SpliceEnclosureCondition,
        Nr33ChecklistAttributesJson = JsonSerializer.Serialize(audit.Nr33ChecklistAttributes, JsonOptions),
        AuditedBy = audit.AuditedBy,
        AuditedAtUtc = audit.AuditedAtUtc,
    };

    public static ManholeAudit ToDomain(ManholeAuditRecord record) => new()
    {
        Id = record.Id,
        ConduitFeatureId = record.ConduitFeatureId,
        OrganizationId = record.OrganizationId,
        BoreOccupancyPercentage = record.BoreOccupancyPercentage,
        SubDucts = JsonSerializer.Deserialize<List<SubDuct>>(record.SubDuctsJson, JsonOptions) ?? [],
        SpliceEnclosureCondition = record.SpliceEnclosureCondition,
        Nr33ChecklistAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(record.Nr33ChecklistAttributesJson, JsonOptions) ?? [],
        AuditedBy = record.AuditedBy,
        AuditedAtUtc = record.AuditedAtUtc,
    };
}
