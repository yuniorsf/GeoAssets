using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Client contract for the mobile offline-sync endpoints <c>GeoAssets.FieldService.Server</c>'s
/// <c>FieldServiceSyncRestApiExtensions</c> already exposes (XD01-208) — pull deltas, push local
/// edits. <see cref="MauiSyncGateway"/> is the sole consumer.
/// </summary>
public interface IFieldServiceSyncClient
{
    /// <summary>Orders created or updated at or after <paramref name="sinceUtc"/>, scoped server-side to the caller's tenant.</summary>
    Task<IReadOnlyList<ServiceOrder>> PullSinceAsync(DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>
    /// Pushes a batch of locally-modified orders, each carrying its own idempotency key
    /// (XD01-236) so a reconnect-burst replay of an already-applied item is recognized and
    /// short-circuited server-side rather than re-applied. Each result reports its own item's
    /// outcome independently — a failure partway through the batch never prevents the rest from
    /// being attempted.
    /// </summary>
    Task<IReadOnlyList<RemoteSyncPushResult>> PushAsync(IReadOnlyList<PushServiceOrderItem> items, CancellationToken ct = default);
}
