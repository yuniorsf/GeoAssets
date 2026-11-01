namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// A single locally-queued mutation awaiting sync to <c>GeoAssets.FieldService.Server</c>
/// (XD01-243). <see cref="SequenceNumber"/> is the strict chronological replay order — assigned
/// by <see cref="ILocalActionOutbox.NextSequenceNumberAsync"/> at enqueue time, independent of
/// <see cref="CreatedAtUtc"/> (a device clock is not monotonic across app restarts the way an
/// incrementing counter is).
/// </summary>
public sealed record LocalOutboxEntry(
    Guid Id,
    long SequenceNumber,
    string ServiceOrderId,
    string ServiceOrderJson,
    DateTime CreatedAtUtc,
    int AttemptCount,
    string? LastError);
