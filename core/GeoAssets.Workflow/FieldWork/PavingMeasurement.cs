namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// One append-only audit row (XD01-291) recording the as-built trench measurements that drove a
/// <see cref="WaterPavingSubOrderSpawner.TransitionToInspectedWithPavingSubOrder"/> spawn —
/// persisted alongside the parent order's status advance, the ledger postings, and the spawned
/// child order, all in the same transaction (<see cref="GeoAssets.Workflow.Persistence.InspectionCommitService"/>).
/// Every property is <c>init</c>-only, same immutable-ledger convention as
/// <see cref="Contractors.ContractorUsPosting"/> — there is no update path, only insert.
/// </summary>
public sealed class PavingMeasurement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The water/sewer repair order this measurement was recorded against.</summary>
    public string ParentServiceOrderId { get; init; } = string.Empty;

    /// <summary>The paving/ICERP restoration child order spawned from this measurement.</summary>
    public string ChildServiceOrderId { get; init; } = string.Empty;

    public Guid OrganizationId { get; init; } = Guid.Empty;

    public double LengthMeters { get; init; }
    public double WidthMeters { get; init; }
    public double DepthMeters { get; init; }
    public double RecordedAreaSquareMeters { get; init; }
    public double ActualAreaSquareMeters { get; init; }
    public double VolumeCubicMeters { get; init; }

    public DateTime RecordedAtUtc { get; init; }
}
