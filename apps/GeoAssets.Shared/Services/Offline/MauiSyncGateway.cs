using System.Text.Json;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Orchestrates one offline-sync cycle for a MAUI field client (XD01-243): flushes
/// <see cref="ILocalActionOutbox"/> in strict chronological order against
/// <see cref="IFieldServiceSyncClient"/>, routes any push conflict through the existing
/// <see cref="ThreeWayConflictReconciler"/> (XD01-223), then pulls server-side deltas since the
/// last successful sync. Pure orchestration over the injected abstractions — no SQLCipher/HTTP
/// concerns of its own, which is what keeps it unit-testable outside MAUI's mobile-only TFMs.
///
/// A failed entry is marked failed and left in the outbox for the caller to decide whether to
/// retry; it is not resubmitted within the same <see cref="SyncAsync"/> call — conceptually the
/// same "no partial, no silent retry storm" shape the server-side outbox relay
/// (<c>GeoAssets.Workflow.Persistence.Outbox.OutboxRelayProcessor</c>) follows, mirrored here
/// rather than reused directly, since pulling in that EFCore-bound project would drag EF Core
/// into a mobile client.
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
        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            var outcome = await FlushOneAsync(entry, ct).ConfigureAwait(false);

            pushed += outcome.Pushed;
            reconciled += outcome.Reconciled;
            failed += outcome.Failed;
            if (outcome.Error is not null) errors.Add(outcome.Error);
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

    private async Task<(int Pushed, int Reconciled, int Failed, string? Error)> FlushOneAsync(
        LocalOutboxEntry entry, CancellationToken ct)
    {
        var localOrder = JsonSerializer.Deserialize<ServiceOrder>(entry.ServiceOrderJson, _opts)
            ?? throw new InvalidOperationException($"Outbox entry '{entry.Id}' has no valid ServiceOrder payload.");

        var pushResults = await syncClient.PushAsync([localOrder], ct).ConfigureAwait(false);
        var result = pushResults.FirstOrDefault(r => string.Equals(r.Id, localOrder.Id, StringComparison.Ordinal));

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
