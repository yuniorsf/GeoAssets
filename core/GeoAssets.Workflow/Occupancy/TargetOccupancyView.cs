using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// Everything about the occupancy of one order target's pole (XD01-315): what is on the pole
/// now, what is staged for it on this order, and what the pole will look like if the staged
/// changes are committed.
///
/// Not paged: it is one pole — its regulated slots (at most six) and its over-capacity entries,
/// which a pole occupancy always carries whole — and one change set, capped at
/// <see cref="OccupancyChangeSet.MaxChanges"/> changes.
/// </summary>
public sealed record TargetOccupancyView
{
    public required string OrderId { get; init; }
    public required string TargetId { get; init; }
    public required string PoleFeatureId { get; init; }

    /// <summary>The organization that owns the pole: the order's, and the caller's own.</summary>
    public required Guid PoleOwnerOrganizationId { get; init; }

    /// <summary>The pole's live occupancy version; 0 when it has none on file.</summary>
    public required int CurrentVersion { get; init; }

    /// <summary>The pole's live occupancy.</summary>
    public required OccupancyProjection Current { get; init; }

    /// <summary>The set staged on this target; null when nothing is staged.</summary>
    public OccupancyChangeSetView? Staged { get; init; }

    /// <summary>The version the staged set was staged against; null when nothing is staged.</summary>
    public int? BaselineVersion { get; init; }

    /// <summary>
    /// True when something is staged and the pole's occupancy has changed since: the staged
    /// changes were decided on a picture of the pole that is no longer current. Staging again
    /// records the current version.
    /// </summary>
    public bool IsBaselineStale { get; init; }

    /// <summary>
    /// The pole after the staged changes, applied to its <b>live</b> occupancy; equal to
    /// <see cref="Current"/> when nothing is staged; null when the staged changes no longer
    /// apply to the pole (see <see cref="Warnings"/>).
    /// </summary>
    public OccupancyProjection? Preview { get; init; }

    /// <summary>
    /// Codes from <see cref="OccupancyProblemCodes"/> describing the staged set:
    /// <see cref="OccupancyProblemCodes.StaleBaseline"/> when <see cref="IsBaselineStale"/>, and
    /// <see cref="OccupancyProblemCodes.EntryNotFound"/> or
    /// <see cref="OccupancyProblemCodes.InvalidChange"/> when it no longer applies. Empty otherwise.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
