namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// One child-order notification as an adapter of the shared-use dispatch staged it, reduced to
/// what <see cref="SharedUseDispatchContractTests"/> asserts — so the suite reads the same thing
/// from a recording fake and from an outbox table.
/// </summary>
public sealed record SharedUseStagedNotification(
    string MessageType,
    string PartitionKey,
    string? CorrelationId,
    string? CausationId,
    string OrderId,
    string OrderTypeId,
    string NewStatus,
    string PerformedBy,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string> Metadata);
