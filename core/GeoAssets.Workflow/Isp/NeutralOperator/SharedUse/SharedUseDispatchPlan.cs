namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// What dispatching a shared-use order would do, computed by <see cref="SharedUseDispatchPlanner"/>
/// from the order's targets and the occupancy of their poles. A plan is a value: making one
/// writes nothing.
/// </summary>
public sealed record SharedUseDispatchPlan
{
    public required string ParentOrderId { get; init; }

    /// <summary>The parent's status once dispatched; its current one when the dispatch does not move it.</summary>
    public required string ParentStatusAfterDispatch { get; init; }

    /// <summary>The single clock reading every instant of the plan carries.</summary>
    public required DateTimeOffset PlannedAt { get; init; }

    /// <summary>How many targets the parent holds.</summary>
    public required int TargetCount { get; init; }

    /// <summary>Targets left out of every child because another open order blocks their pole.</summary>
    public required int BlockedTargetCount { get; init; }

    /// <summary>
    /// One child per occupant organization with an active contract on at least one pole that is
    /// not blocked, in the order of the organization id's canonical text.
    /// </summary>
    public required IReadOnlyList<SharedUsePlannedChild> Children { get; init; }

    /// <summary>
    /// Every occupancy entry on a pole that is not blocked which gets no dispatch, ordered by pole
    /// feature id, then by the entry id's canonical text.
    /// </summary>
    public required IReadOnlyList<SharedUseSkippedEntry> SkippedEntries { get; init; }

    /// <summary>
    /// Poles that are not blocked and have nobody to notify — no occupancy on file, or no entry
    /// that qualifies — in feature id order. They do not stop the dispatch.
    /// </summary>
    public required IReadOnlyList<string> UnnotifiablePoleIds { get; init; }

    /// <summary>Why the order cannot be dispatched as it stands; null when it can.</summary>
    public SharedUseDispatchBlockReason? BlockReason { get; init; }

    /// <summary>
    /// For <see cref="SharedUseDispatchBlockReason.TargetsWithoutActivity"/>: every target that is
    /// not blocked and has no activity, in target id order. Empty otherwise.
    /// </summary>
    public IReadOnlyList<string> TargetIdsWithoutActivity { get; init; } = [];

    /// <summary>The children a dispatch would create now: those the parent does not have yet.</summary>
    public IEnumerable<SharedUsePlannedChild> NewChildren => Children.Where(child => !child.AlreadyDispatched);

    /// <summary>Throws <see cref="SharedUseDispatchBlockedException"/> when <see cref="BlockReason"/> is set.</summary>
    public void EnsureDispatchable()
    {
        if (BlockReason is not { } reason) return;

        throw new SharedUseDispatchBlockedException(
            ParentOrderId,
            reason,
            [.. TargetIdsWithoutActivity.Take(SharedUseDispatchBlockedException.MaxReportedTargetIds)],
            TargetIdsWithoutActivity.Count);
    }
}
