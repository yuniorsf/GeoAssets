namespace GeoAssets.NeutralOperator.Application.SharedUse;

/// <summary>What one dispatch of a shared-use order did. Counts only: the child orders are the order's children.</summary>
public sealed record SharedUseDispatchResult
{
    public required string ParentOrderId { get; init; }

    public required string OrderTypeId { get; init; }

    /// <summary>The parent's status after the call.</summary>
    public required string ParentStatus { get; init; }

    /// <summary>Child orders this call created; zero when every recipient already had one.</summary>
    public required int ChildOrdersCreated { get; init; }

    /// <summary>Recipients whose child order already existed and was left as it was.</summary>
    public required int ChildOrdersAlreadyDispatched { get; init; }

    /// <summary>Notifications staged on the outbox: one per child order created.</summary>
    public required int NotificationsEnqueued { get; init; }

    public required int TargetCount { get; init; }

    /// <summary>Targets left out because another open order blocks their pole.</summary>
    public required int BlockedTargetCount { get; init; }

    /// <summary>Poles that are not blocked and had nobody to notify.</summary>
    public required int UnnotifiablePoleCount { get; init; }

    /// <summary>Occupancy entries on the order's poles that got no dispatch.</summary>
    public required int SkippedEntryCount { get; init; }
}
