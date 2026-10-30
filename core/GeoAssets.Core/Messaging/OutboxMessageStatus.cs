namespace GeoAssets.Core.Messaging;

/// <summary>
/// Lifecycle state of an integration message persisted in the transactional outbox.
/// </summary>
public enum OutboxMessageStatus
{
    Pending = 0,
    Processing = 1,
    Dispatched = 2,
    Failed = 3,
}
