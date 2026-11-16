using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// An <see cref="OccupancyChangeSet"/> as the staging service hands it out and the REST routes
/// carry it (XD01-315): the same data, as a plain record a serializer can write and read back.
/// </summary>
public sealed record OccupancyChangeSetView
{
    public required Guid Id { get; init; }
    public required string OrderId { get; init; }
    public required string TargetId { get; init; }
    public required string PoleFeatureId { get; init; }
    public required OccupancyChangeSetStatus Status { get; init; }

    /// <summary>The pole's occupancy version the set was staged against (0 when the pole had none on file).</summary>
    public required int BaselineVersion { get; init; }

    public required string StagedBy { get; init; }
    public required DateTimeOffset StagedAt { get; init; }

    /// <summary>When the set was committed or discarded; null while staged.</summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>The changes, in the order they are applied.</summary>
    public IReadOnlyList<OccupancyChange> Changes { get; init; } = [];

    /// <exception cref="ArgumentNullException"><paramref name="changeSet"/> is null.</exception>
    public static OccupancyChangeSetView From(OccupancyChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        return new OccupancyChangeSetView
        {
            Id = changeSet.Id,
            OrderId = changeSet.OrderId,
            TargetId = changeSet.TargetId,
            PoleFeatureId = changeSet.PoleFeatureId,
            Status = changeSet.Status,
            BaselineVersion = changeSet.BaselineVersion,
            StagedBy = changeSet.StagedBy,
            StagedAt = changeSet.StagedAt,
            ClosedAt = changeSet.ClosedAt,
            Changes = changeSet.Changes,
        };
    }
}
