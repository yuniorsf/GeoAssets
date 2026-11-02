using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Orchestrates one offline-sync cycle for a MAUI field client (XD01-243): flushes
/// <see cref="ILocalActionOutbox"/> in strict chronological order against
/// <see cref="IFieldServiceSyncClient"/> as a single idempotency-keyed batch (XD01-236 — each
/// item carries its own outbox entry Id, so a reconnect-burst replay of the same batch is
/// deduped server-side instead of double-applied), routes any push conflict through the
/// existing <see cref="ThreeWayConflictReconciler"/> (XD01-223), then pulls server-side deltas
/// since the last successful sync. Pure orchestration over the injected abstractions — no
/// SQLCipher/HTTP concerns of its own, which is what keeps it unit-testable outside MAUI's
/// mobile-only TFMs.
///
/// A failed entry is marked failed and left in the outbox for the caller to decide whether to
/// retry; it is not resubmitted within the same <see cref="SyncAsync"/> call, and one entry's
/// failure never prevents the rest of the batch's own entries from being applied or blocks a
/// subsequent retry from re-sending it alone — the local outbox table only ever marks the
/// entries that actually succeeded or failed, never the whole batch at once.
/// </summary>
public sealed class MauiSyncGateway(
    ILocalServiceOrderStore localStore,
    ILocalActionOutbox outbox,
    ILocalSyncCursorStore cursorStore,
    IFieldServiceSyncClient syncClient,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions _opts = GeoJsonSerializer.Options;

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        var pushed = 0;
        var reconciled = 0;
        var failed = 0;
        var errors = new List<string>();

        var pending = await outbox.GetPendingInOrderAsync(ct).ConfigureAwait(false);
        if (pending.Count > 0)
        {
            // One batch round-trip, each item carrying its own outbox entry Id as the
            // idempotency key (XD01-236) — a reconnect burst that replays this exact batch
            // (the client never saw the first attempt's ack) is deduped server-side per item,
            // not re-applied. Items are parsed/sent in strict outbox order, but each item's
            // outcome is still independent: one failing never blocks or corrupts the rest.
            var items = pending
                .Select(entry => new PushServiceOrderItem(DeserializeOrder(entry), entry.Id.ToString("D")))
                .ToList();
            var results = await syncClient.PushAsync(items, ct).ConfigureAwait(false);

            for (var i = 0; i < pending.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var result = i < results.Count ? results[i] : null;
                var outcome = await HandlePushResultAsync(pending[i], items[i].Order, result, ct).ConfigureAwait(false);

                pushed += outcome.Pushed;
                reconciled += outcome.Reconciled;
                failed += outcome.Failed;
                if (outcome.Error is not null) errors.Add(outcome.Error);
            }
        }

        var sinceUtc = await cursorStore.GetLastSyncedUtcAsync(ct).ConfigureAwait(false);
        var pulled = await syncClient.PullSinceAsync(sinceUtc, ct).ConfigureAwait(false);
        foreach (var remote in pulled)
        {
            await localStore.UpsertAsync(remote, ct).ConfigureAwait(false);
            await localStore.SaveBaseSnapshotAsync(remote, ct).ConfigureAwait(false);
        }

        await cursorStore.SetLastSyncedUtcAsync(timeProvider.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return new SyncResult(pushed, pulled.Count, reconciled, failed, errors);
    }

    private static ServiceOrder DeserializeOrder(LocalOutboxEntry entry) =>
        JsonSerializer.Deserialize<ServiceOrder>(entry.ServiceOrderJson, _opts)
            ?? throw new InvalidOperationException($"Outbox entry '{entry.Id}' has no valid ServiceOrder payload.");

    private async Task<(int Pushed, int Reconciled, int Failed, string? Error)> HandlePushResultAsync(
        LocalOutboxEntry entry, ServiceOrder localOrder, RemoteSyncPushResult? result, CancellationToken ct)
    {
        if (result is { Success: true })
        {
            await outbox.MarkDispatchedAsync(entry.Id, ct).ConfigureAwait(false);
            await localStore.SaveBaseSnapshotAsync(localOrder, ct).ConfigureAwait(false);
            return (1, 0, 0, null);
        }

        var error = result?.Error ?? "No result returned for this ServiceOrder.";
        await outbox.MarkFailedAsync(entry.Id, error, ct).ConfigureAwait(false);

        var reconciledOrder = await TryReconcileAsync(localOrder, ct).ConfigureAwait(false);
        if (reconciledOrder is not null)
        {
            await localStore.UpsertAsync(reconciledOrder, ct).ConfigureAwait(false);
            await localStore.SaveBaseSnapshotAsync(reconciledOrder, ct).ConfigureAwait(false);
            return (0, 1, 0, null);
        }

        return (0, 0, 1, $"{localOrder.Id}: {error}");
    }

    private async Task<ServiceOrder?> TryReconcileAsync(ServiceOrder localOrder, CancellationToken ct)
    {
        var baseOrder = await localStore.GetBaseSnapshotAsync(localOrder.Id, ct).ConfigureAwait(false);
        if (baseOrder is null) return null;

        var remoteCandidates = await syncClient.PullSinceAsync(
            baseOrder.UpdatedAt?.AddSeconds(-1) ?? DateTime.MinValue, ct).ConfigureAwait(false);
        var remoteOrder = remoteCandidates.FirstOrDefault(o => string.Equals(o.Id, localOrder.Id, StringComparison.Ordinal));
        if (remoteOrder is null) return null;

        return ThreeWayConflictReconciler.Reconcile(baseOrder, localOrder, remoteOrder, timeProvider);
    }
}
