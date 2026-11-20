namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// What <see cref="IOccupancyApplySagaStore.CompleteAsync"/> did (XD01-392): whether it ended the saga and, for a revert,
/// which of its poles the compensation left untouched because a later change had moved them past the version the approval
/// left them at — that later state wins.
/// </summary>
/// <param name="Completed">False when the saga was no longer pending and nothing changed.</param>
/// <param name="UnrestoredFeatureIds">The poles a revert did not restore, ordinal order; empty otherwise.</param>
public sealed record OccupancyApplySagaCompletion(bool Completed, IReadOnlyList<string> UnrestoredFeatureIds)
{
    /// <summary>The saga was no longer pending; nothing changed.</summary>
    public static OccupancyApplySagaCompletion NotPending { get; } = new(false, []);

    /// <summary>The saga ended and, for a revert, every pole was restored.</summary>
    public static OccupancyApplySagaCompletion Done { get; } = new(true, []);
}
