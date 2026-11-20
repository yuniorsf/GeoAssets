namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// What the saga timeout sweeper reads and writes (XD01-383): across every organization, since it serves no caller — the
/// saga headers it touches hold ids, states and instants only (the poles' snapshots stay behind the tenant scope).
/// </summary>
public interface IOccupancyApplySagaMonitor
{
    /// <summary>
    /// Flags up to <paramref name="max"/> pending sagas last staged before <paramref name="staleBefore"/> and not flagged yet
    /// as timed out at <paramref name="now"/>, and returns them. Their state stays pending.
    /// </summary>
    Task<IReadOnlyList<OccupancyApplySaga>> MarkTimedOutAsync(DateTimeOffset staleBefore, DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>How many sagas are pending, and when the oldest of them was last staged (null when none is).</summary>
    Task<(int Pending, DateTimeOffset? OldestLastRequestedAt)> GetBacklogAsync(CancellationToken ct);
}
