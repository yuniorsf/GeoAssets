using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>What an approval committed (XD01-318). Counts only: the sets themselves are read, paged, through the staging service.</summary>
public sealed record OccupancyCommitResult
{
    public required string OrderId { get; init; }

    public required string OrderTypeId { get; init; }

    /// <summary>The order's status before the approval.</summary>
    public required string PreviousStatus { get; init; }

    /// <summary>The order's status after the approval.</summary>
    public required string Status { get; init; }

    /// <summary>Staged sets applied to their poles and marked committed.</summary>
    public required int ChangeSetsCommitted { get; init; }

    /// <summary>Staged sets on targets that were skipped or found not executable: discarded, not committed.</summary>
    public required int ChangeSetsDiscarded { get; init; }

    /// <summary>Changes in the committed sets, all kinds together.</summary>
    public required int ChangesCommitted { get; init; }

    /// <summary>Changes in the committed sets, per kind; a kind with none is absent.</summary>
    public required IReadOnlyDictionary<OccupancyChangeKind, int> ChangesByKind { get; init; }

    /// <summary>Poles whose live occupancy was written: one per committed set.</summary>
    public required int PolesUpdated { get; init; }

    /// <summary>
    /// Id of the asset mutation batch staged on the outbox to refresh the poles' occupancy
    /// attributes; null when no pole was written, and then nothing was staged.
    /// </summary>
    public Guid? AssetMutationBatchId { get; init; }
}
