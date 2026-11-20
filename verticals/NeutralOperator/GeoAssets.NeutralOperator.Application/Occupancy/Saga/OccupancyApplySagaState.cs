namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>Where an occupancy approval's asset-mutation apply saga is (XD01-383): <c>pending → committed | reverted</c>.</summary>
public enum OccupancyApplySagaState
{
    /// <summary>
    /// The approval wrote the poles' occupancy and staged its asset mutation batch; the kernel has not answered yet. A saga
    /// past its timeout stays pending (it is flagged, never silently reverted) until the kernel answers or an operator retries.
    /// </summary>
    Pending = 0,

    /// <summary>The kernel applied the batch: the occupancy is final and the order moved on to its approved status.</summary>
    Committed = 1,

    /// <summary>The kernel rejected the batch: the poles' occupancy was restored and the order stayed where it was before the approval.</summary>
    Reverted = 2,
}
