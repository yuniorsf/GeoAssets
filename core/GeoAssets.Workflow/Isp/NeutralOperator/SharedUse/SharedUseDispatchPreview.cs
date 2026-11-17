using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// What dispatching a shared-use order would do right now, without doing it: the counts of the
/// whole plan and one page of its recipients.
/// </summary>
public sealed record SharedUseDispatchPreview
{
    public required string OrderId { get; init; }

    public required string OrderTypeId { get; init; }

    /// <summary>The order type of the child orders.</summary>
    public required string ChildOrderTypeId { get; init; }

    /// <summary>
    /// The order's concurrency token as read for this preview: what a caller hands to
    /// <see cref="ISharedUseDispatchService.DispatchAsync"/> to dispatch exactly the order it saw.
    /// </summary>
    public required byte[] RowVersion { get; init; }

    /// <summary>The order's status once dispatched.</summary>
    public required string StatusAfterDispatch { get; init; }

    public required int TargetCount { get; init; }

    /// <summary>Targets left out because another open order blocks their pole.</summary>
    public required int BlockedTargetCount { get; init; }

    /// <summary>Targets that are not blocked and have no activity assigned; any makes the order undispatchable.</summary>
    public required int TargetsWithoutActivityCount { get; init; }

    /// <summary>Poles that are not blocked and have nobody to notify.</summary>
    public required int UnnotifiablePoleCount { get; init; }

    /// <summary>Occupancy entries on the order's poles that get no dispatch.</summary>
    public required int SkippedEntryCount { get; init; }

    /// <summary>Recipients that do not have their child order yet: what a dispatch would create.</summary>
    public required int NewRecipientCount { get; init; }

    /// <summary>Why the order cannot be dispatched as it stands; null when it can.</summary>
    public SharedUseDispatchBlockReason? BlockReason { get; init; }

    /// <summary>True when a dispatch now would create at least one child order.</summary>
    public required bool CanDispatch { get; init; }

    /// <summary>
    /// The page of recipients asked for, ordered by the organization id's canonical text;
    /// <see cref="PagedResult{T}.TotalCount"/> is the number of recipients in all.
    /// </summary>
    public required PagedResult<SharedUseDispatchRecipient> Recipients { get; init; }
}
