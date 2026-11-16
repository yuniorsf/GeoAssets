namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>Where an <see cref="OccupancyChangeSet"/> is in its life (XD01-315).</summary>
public enum OccupancyChangeSetStatus
{
    /// <summary>Proposed on an order's target; the pole's live occupancy is untouched. At most one per target.</summary>
    Staged = 0,

    /// <summary>Applied to the pole's live occupancy when the order was approved. Final.</summary>
    Committed = 1,

    /// <summary>Dropped: by the crew, or because the order closed without it being committed. Final.</summary>
    Discarded = 2,
}
