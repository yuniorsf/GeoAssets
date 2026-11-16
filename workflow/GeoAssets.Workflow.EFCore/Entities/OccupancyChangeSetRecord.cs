namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>ServiceOrderOccupancyChangeSets</c> table (XD01-315): the
/// occupancy changes proposed for one pole through one target of one service order. Kept
/// separate from the domain
/// <see cref="Core.Isp.NeutralOperator.Occupancy.OccupancyChangeSet"/> so the domain model stays
/// free of EF annotations.
/// </summary>
internal sealed class OccupancyChangeSetRecord
{
    public Guid Id { get; set; }

    /// <summary>The order the set was staged on. Repeated from the target so an order's sets are one index read.</summary>
    public string ServiceOrderId { get; set; } = string.Empty;

    /// <summary>FK to the <see cref="ServiceOrderTargetRecord"/> the set was staged on; its sets are deleted with it.</summary>
    public string TargetId { get; set; } = string.Empty;

    public string PoleFeatureId { get; set; } = string.Empty;

    /// <summary>JSON array of the changes, in the order they are applied (see <c>OccupancyChangeSetMapper</c>).</summary>
    public string ChangesJson { get; set; } = "[]";

    /// <summary>The pole's occupancy version the set was staged against; 0 when the pole had none on file.</summary>
    public int BaselineVersion { get; set; }

    /// <summary><c>OccupancyChangeSetStatus</c> by member name. At most one <c>Staged</c> row per target.</summary>
    public string Status { get; set; } = string.Empty;

    public string StagedBy { get; set; } = string.Empty;

    public DateTime StagedAt { get; set; }

    /// <summary>UTC instant the set was committed or discarded; null while staged.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>The organization that owns the pole — and the order, its target and this row.</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;
}
