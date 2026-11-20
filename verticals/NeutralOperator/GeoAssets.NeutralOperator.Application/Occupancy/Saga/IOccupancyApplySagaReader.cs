namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// The read side of the approval sagas' store (XD01-383), scoped like the occupancy stores to the caller's organization and
/// reading in the caller's unit of work when one is open.
/// </summary>
public interface IOccupancyApplySagaReader
{
    /// <summary>The saga of <paramref name="batchId"/>, or null when this organization has none (another vertical's batch, another organization's).</summary>
    Task<OccupancyApplySaga?> FindAsync(Guid batchId, CancellationToken ct);

    /// <summary>The order's latest saga (highest <see cref="OccupancyApplySaga.Attempt"/>), or null when it has none.</summary>
    Task<OccupancyApplySaga?> FindLatestForOrderAsync(string orderId, CancellationToken ct);

    /// <summary>Which of <paramref name="poleFeatureIds"/> an open (pending) saga holds, ordinal order, at most one entry each.</summary>
    Task<IReadOnlyList<string>> FindPendingPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct);

    /// <summary>The poles of <paramref name="batchId"/>'s saga, ordinal order.</summary>
    Task<IReadOnlyList<OccupancyApplySagaPole>> GetPolesAsync(Guid batchId, CancellationToken ct);
}
