namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Store-and-forward queue of locally-made <see cref="GeoAssets.Workflow.Orders.ServiceOrder"/>
/// mutations awaiting sync (XD01-243). <see cref="GetPendingInOrderAsync"/> must return entries by
/// <see cref="LocalOutboxEntry.SequenceNumber"/> ascending — <see cref="MauiSyncGateway"/> replays
/// them in that exact order on reconnect, the AC's "no data loss, strict chronological replay"
/// requirement.
/// </summary>
public interface ILocalActionOutbox
{
    /// <summary>Monotonically increasing per-device counter — never reused, even across app restarts.</summary>
    Task<long> NextSequenceNumberAsync(CancellationToken ct = default);

    Task EnqueueAsync(LocalOutboxEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<LocalOutboxEntry>> GetPendingInOrderAsync(CancellationToken ct = default);

    Task MarkDispatchedAsync(Guid entryId, CancellationToken ct = default);

    Task MarkFailedAsync(Guid entryId, string error, CancellationToken ct = default);
}
