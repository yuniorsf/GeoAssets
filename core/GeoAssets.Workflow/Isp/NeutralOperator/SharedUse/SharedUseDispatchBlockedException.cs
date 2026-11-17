namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Thrown when a shared-use order cannot be dispatched as it stands — see
/// <see cref="SharedUseDispatchBlockReason"/>. Nothing was written.
/// </summary>
public sealed class SharedUseDispatchBlockedException : InvalidOperationException
{
    /// <summary>How many offending target ids <see cref="TargetIds"/> carries at most.</summary>
    public const int MaxReportedTargetIds = 100;

    public SharedUseDispatchBlockedException(
        string orderId, SharedUseDispatchBlockReason reason, IReadOnlyList<string>? targetIds = null, int? targetCount = null)
        : base(MessageFor(orderId, reason))
    {
        OrderId = orderId;
        Reason = reason;
        TargetIds = targetIds ?? [];
        TargetCount = targetCount ?? TargetIds.Count;
    }

    public SharedUseDispatchBlockedException()
    {
    }

    public SharedUseDispatchBlockedException(string message) : base(message)
    {
    }

    public SharedUseDispatchBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The order that cannot be dispatched; null when not known.</summary>
    public string? OrderId { get; }

    public SharedUseDispatchBlockReason Reason { get; }

    /// <summary>
    /// For <see cref="SharedUseDispatchBlockReason.TargetsWithoutActivity"/>: the first
    /// <see cref="MaxReportedTargetIds"/> offending target ids, in target-id order. Empty otherwise.
    /// </summary>
    public IReadOnlyList<string> TargetIds { get; } = [];

    /// <summary>How many targets offend in all; <see cref="TargetIds"/> may list fewer.</summary>
    public int TargetCount { get; }

    private static string MessageFor(string orderId, SharedUseDispatchBlockReason reason) => reason switch
    {
        SharedUseDispatchBlockReason.NoTargets =>
            $"Service order '{orderId}' has no target to dispatch.",
        SharedUseDispatchBlockReason.TargetsWithoutActivity =>
            $"Service order '{orderId}' has targets with no activity assigned.",
        SharedUseDispatchBlockReason.AllTargetsBlocked =>
            $"Every target of service order '{orderId}' is blocked by another open order.",
        SharedUseDispatchBlockReason.NoEligibleOccupants =>
            $"No pole of service order '{orderId}' has an occupant with an active contract and a known organization.",
        _ => $"Service order '{orderId}' cannot be dispatched.",
    };
}
