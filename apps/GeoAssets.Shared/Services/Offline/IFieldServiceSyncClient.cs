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

    /// <summary>Pushes locally-modified orders; each result reports its own order's outcome independently.</summary>
    Task<IReadOnlyList<RemoteSyncPushResult>> PushAsync(IReadOnlyList<ServiceOrder> orders, CancellationToken ct = default);
}
