namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>Where an <see cref="OccupancyChangeSet"/> is in its life (XD01-315).</summary>
public enum OccupancyChangeSetStatus
{
    /// <summary>Proposed on an order's target; the pole's live occupancy is untouched. At most one per target.</summary>
    Staged = 0,

    /// <summary>
    /// Applied to the pole's live occupancy and confirmed by the asset catalog (XD01-383: the kernel applied the approval's
    /// asset mutation batch) — or, before the approval saga, applied when the order was approved. Final.
    /// </summary>
    Committed = 1,

    /// <summary>Dropped: by the crew, or because the order closed without it being committed. Final.</summary>
    Discarded = 2,

    /// <summary>
    /// Applied to the pole's live occupancy by an approval whose asset mutation batch the kernel has not answered yet
    /// (XD01-383): becomes <see cref="Committed"/> when the kernel applies the batch, <see cref="Reverted"/> when it rejects it.
    /// </summary>
    Pending = 3,

    /// <summary>
    /// The kernel rejected the approval's asset mutation batch (XD01-383): the pole's occupancy was restored to what it was
    /// before the approval. Final — the changes must be staged again.
    /// </summary>
    Reverted = 4,
}
