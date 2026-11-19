namespace GeoAssets.Workflow.Notifications;

/// <summary>
/// What the global target ledger reports when an order's targets change (XD01-379): staged on the transactional outbox
/// in the same transaction as the ledger change, as one of <see cref="TargetsReservedEvent"/>,
/// <see cref="TargetsReleasedEvent"/> or <see cref="TargetsCommittedEvent"/>. Written only when something changed.
/// </summary>
public abstract record TargetLedgerEvent
{
    /// <summary>The vertical that owns <see cref="OrderId"/>.</summary>
    public required string VerticalId { get; init; }

    /// <summary>The order whose targets changed.</summary>
    public required string OrderId { get; init; }

    /// <summary>The organization the targets belong to.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The targets that changed, each with its feature.</summary>
    public required IReadOnlyList<TargetLedgerEventItem> Targets { get; init; }

    /// <summary>UTC instant of the change, read from the ledger's <see cref="TimeProvider"/>.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The actor recorded for the change; null when none was given.</summary>
    public string? PerformedBy { get; init; }
}
