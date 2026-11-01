using GeoAssets.Workflow.Contractors;

namespace GeoAssets.Workflow.Persistence;

/// <summary>Result of <see cref="InspectionCommitService.CommitAsync"/> (XD01-228).</summary>
public sealed class InspectionCommitResult
{
    public string ServiceOrderId { get; init; } = string.Empty;
    public decimal TotalUs { get; init; }
    public decimal TotalBrl { get; init; }
    public IReadOnlyList<ContractorUsPosting> Postings { get; init; } = [];
    public Guid OutboxMessageId { get; init; }

    /// <summary>The spawned paving/ICERP restoration child order's id, or null when the inspected order wasn't paving-eligible (XD01-291).</summary>
    public string? PavingChildServiceOrderId { get; init; }
}
