using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Services.Offline;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Offline;

/// <summary>
/// Exercises <see cref="MauiSyncGateway.SyncAsync"/>'s push/reconcile/pull pipeline (XD01-243)
/// against hand-written in-memory fakes of its four collaborator ports — no mocking library,
/// consistent with this codebase's precedent for multi-method domain-shaped ports (see
/// <c>GeoCoreBridgeEventPublisherTests</c>).
/// </summary>
public class MauiSyncGatewayTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeLocalServiceOrderStore : ILocalServiceOrderStore
    {
        private readonly Dictionary<string, ServiceOrder> _orders = [];
        private readonly Dictionary<string, ServiceOrder> _baseSnapshots = [];

        public Task<ServiceOrder?> GetAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_orders.GetValueOrDefault(serviceOrderId));

        public Task<IReadOnlyList<ServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ServiceOrder>>(_orders.Values.ToList());

        public Task UpsertAsync(ServiceOrder order, CancellationToken ct = default)
        {
            _orders[order.Id] = order;
            return Task.CompletedTask;
        }

        public Task<ServiceOrder?> GetBaseSnapshotAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_baseSnapshots.GetValueOrDefault(serviceOrderId));

        public Task SaveBaseSnapshotAsync(ServiceOrder order, CancellationToken ct = default)
        {
            _baseSnapshots[order.Id] = order;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalActionOutbox : ILocalActionOutbox
    {
        private readonly List<LocalOutboxEntry> _entries = [];
        private long _nextSequence = 1;

        public List<Guid> DispatchedIds { get; } = [];
        public List<(Guid Id, string Error)> FailedCalls { get; } = [];

        public void Seed(LocalOutboxEntry entry) => _entries.Add(entry);

        public Task<long> NextSequenceNumberAsync(CancellationToken ct = default) =>
            Task.FromResult(_nextSequence++);

        public Task EnqueueAsync(LocalOutboxEntry entry, CancellationToken ct = default)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalOutboxEntry>> GetPendingInOrderAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LocalOutboxEntry>>(
                _entries.OrderBy(e => e.SequenceNumber).ToList());

        public Task MarkDispatchedAsync(Guid entryId, CancellationToken ct = default)
        {
            DispatchedIds.Add(entryId);
            _entries.RemoveAll(e => e.Id == entryId);
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(Guid entryId, string error, CancellationToken ct = default)
        {
            FailedCalls.Add((entryId, error));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocalSyncCursorStore(DateTime initialLastSyncedUtc) : ILocalSyncCursorStore
    {
        public DateTime LastSyncedUtc { get; private set; } = initialLastSyncedUtc;
        public int SetCallCount { get; private set; }

        public Task<DateTime> GetLastSyncedUtcAsync(CancellationToken ct = default) =>
            Task.FromResult(LastSyncedUtc);

        public Task SetLastSyncedUtcAsync(DateTime lastSyncedUtc, CancellationToken ct = default)
        {
            LastSyncedUtc = lastSyncedUtc;
            SetCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFieldServiceSyncClient : IFieldServiceSyncClient
    {
        public List<DateTime> PullSinceCalls { get; } = [];
        public List<string> PushedOrderIdsInCallOrder { get; } = [];

        public Func<DateTime, IReadOnlyList<ServiceOrder>> PullSinceHandler { get; set; } = _ => [];
        public Func<IReadOnlyList<ServiceOrder>, IReadOnlyList<RemoteSyncPushResult>> PushHandler { get; set; } = _ => [];

        public Task<IReadOnlyList<ServiceOrder>> PullSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
        {
            PullSinceCalls.Add(sinceUtc);
            return Task.FromResult(PullSinceHandler(sinceUtc));
        }

        public Task<IReadOnlyList<RemoteSyncPushResult>> PushAsync(IReadOnlyList<ServiceOrder> orders, CancellationToken ct = default)
        {
            PushedOrderIdsInCallOrder.AddRange(orders.Select(o => o.Id));
            return Task.FromResult(PushHandler(orders));
        }
    }

    // ── Builders ─────────────────────────────────────────────────────────────

    private static ServiceOrder Order(
        string id,
        string status,
        string? assignedTo = null,
        DateTime? updatedAt = null,
        Dictionary<string, string>? attributes = null,
        string title = "") =>
        new()
        {
            Id = id,
            Status = status,
            AssignedTo = assignedTo,
            UpdatedAt = updatedAt,
            Attributes = attributes ?? [],
            Title = title,
        };

    private static LocalOutboxEntry Entry(ServiceOrder order, long sequenceNumber) =>
        new(
            Guid.NewGuid(),
            sequenceNumber,
            order.Id,
            JsonSerializer.Serialize(order, GeoJsonSerializer.Options),
            CreatedAtUtc: DateTime.UnixEpoch,
            AttemptCount: 0,
            LastError: null);

    private static MauiSyncGateway Sut(
        FakeLocalServiceOrderStore localStore,
        FakeLocalActionOutbox outbox,
        FakeLocalSyncCursorStore cursorStore,
        FakeFieldServiceSyncClient syncClient,
        FakeTimeProvider timeProvider) =>
        new(localStore, outbox, cursorStore, syncClient, timeProvider);

    // ── Scenarios ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_EmptyOutboxNoRemoteChanges_ReturnsAllZerosAndAdvancesCursor()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var client = new FakeFieldServiceSyncClient();
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Pushed.Should().Be(0);
        result.Pulled.Should().Be(0);
        result.Reconciled.Should().Be(0);
        result.Failed.Should().Be(0);
        result.Errors.Should().BeEmpty();
        cursorStore.LastSyncedUtc.Should().Be(timeProvider.GetUtcNow().UtcDateTime);
        cursorStore.SetCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SyncAsync_OnePendingEntryPushesSuccessfully_MarksDispatchedAndSavesBaseSnapshot()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = orders => orders.Select(o => new RemoteSyncPushResult(o.Id, true, null)).ToList(),
        };
        var order = Order("order-1", FieldWorkStatus.Pending);
        var entry = Entry(order, sequenceNumber: 1);
        outbox.Seed(entry);
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Pushed.Should().Be(1);
        outbox.DispatchedIds.Should().ContainSingle().Which.Should().Be(entry.Id);
        (await outbox.GetPendingInOrderAsync()).Should().BeEmpty();
        var savedBase = await localStore.GetBaseSnapshotAsync("order-1");
        savedBase.Should().NotBeNull();
        savedBase!.Status.Should().Be(FieldWorkStatus.Pending);
    }

    /// <summary>
    /// XD01-276's AC: "Outbox flush is idempotent — reconnecting twice doesn't double-submit a
    /// queued action." A connectivity-triggered reconnect just calls <see cref="MauiSyncGateway.SyncAsync"/>
    /// again (see <c>GeoAssets.MAUI.Services.Offline.ConnectivitySyncTrigger</c>); this proves
    /// the second call finds nothing left pending once the first call's dispatched entry is
    /// excluded from <see cref="FakeLocalActionOutbox.GetPendingInOrderAsync"/>, so the sync
    /// client's <c>PushAsync</c> is never invoked a second time for the same entry.
    /// </summary>
    [Fact]
    public async Task SyncAsync_CalledTwiceAfterSuccessfulPush_SecondCallPushesNothingAdditional()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = orders => orders.Select(o => new RemoteSyncPushResult(o.Id, true, null)).ToList(),
        };
        var order = Order("order-1", FieldWorkStatus.Pending);
        var entry = Entry(order, sequenceNumber: 1);
        outbox.Seed(entry);
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var first = await sut.SyncAsync();
        var second = await sut.SyncAsync();

        first.Pushed.Should().Be(1);
        second.Pushed.Should().Be(0, "the entry dispatched in the first call must not be re-flushed");
        client.PushedOrderIdsInCallOrder.Should().ContainSingle().Which.Should().Be("order-1");
    }

    [Fact]
    public async Task SyncAsync_MultiplePendingEntriesOutOfInsertionOrder_FlushesInAscendingSequenceOrder()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = orders => orders.Select(o => new RemoteSyncPushResult(o.Id, true, null)).ToList(),
        };
        var orderHighSeq = Order("order-high-seq", FieldWorkStatus.Pending);
        var orderLowSeq = Order("order-low-seq", FieldWorkStatus.Pending);
        // Enqueued out of SequenceNumber order deliberately.
        outbox.Seed(Entry(orderHighSeq, sequenceNumber: 5));
        outbox.Seed(Entry(orderLowSeq, sequenceNumber: 2));
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        await sut.SyncAsync();

        client.PushedOrderIdsInCallOrder.Should().Equal("order-low-seq", "order-high-seq");
    }

    [Fact]
    public async Task SyncAsync_PushFailsAndNoBaseSnapshotExists_RecordsFailureWithoutAttemptingReconciliation()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = _ => [new RemoteSyncPushResult("order-1", false, "validation failed")],
        };
        var order = Order("order-1", FieldWorkStatus.Pending);
        outbox.Seed(Entry(order, sequenceNumber: 1));
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Failed.Should().Be(1);
        result.Reconciled.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Be("order-1: validation failed");
        // TryReconcileAsync short-circuits before ever calling PullSinceAsync when there is no
        // base snapshot — the single recorded call below is the unconditional delta pull that
        // always runs after the flush loop, not a conflict-retry attempt.
        client.PullSinceCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task SyncAsync_PushFailsBaseSnapshotExistsAndRemoteMatchesBase_ReconcilesAsTrivialCatchUp()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var baseUpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseOrder = Order("order-1", FieldWorkStatus.Pending, updatedAt: baseUpdatedAt, title: "Base");
        await localStore.SaveBaseSnapshotAsync(baseOrder);

        // Same Status/AssignedTo/Attributes as base => Classify sees no real local edit.
        var localOrder = Order("order-1", FieldWorkStatus.Pending, updatedAt: baseUpdatedAt, title: "Local");
        var remoteOrder = Order("order-1", FieldWorkStatus.Pending, updatedAt: baseUpdatedAt.AddHours(1), title: "Remote");

        var retryTimestamp = baseUpdatedAt.AddSeconds(-1);
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = _ => [new RemoteSyncPushResult("order-1", false, "stale")],
            PullSinceHandler = sinceUtc => sinceUtc == retryTimestamp ? [remoteOrder] : [],
        };
        outbox.Seed(Entry(localOrder, sequenceNumber: 1));
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Reconciled.Should().Be(1);
        result.Failed.Should().Be(0);
        var saved = await localStore.GetAsync("order-1");
        saved!.Title.Should().Be("Remote");
    }

    [Fact]
    public async Task SyncAsync_PushFailsAndThreeWayDiffIsStructuralCollision_EscalatesToConflictReview()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var baseUpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseOrder = Order("order-1", FieldWorkStatus.OnSite, assignedTo: "crew-1", updatedAt: baseUpdatedAt);
        await localStore.SaveBaseSnapshotAsync(baseOrder);

        // Local completed the work; remote independently moved to a different, non-Completed
        // status while offline => ThreeWayConflictReconciler.Classify returns StructuralCollision.
        var localOrder = Order("order-1", FieldWorkStatus.Completed, assignedTo: "crew-1", updatedAt: baseUpdatedAt);
        var remoteOrder = Order("order-1", FieldWorkStatus.Pending, assignedTo: "crew-1", updatedAt: baseUpdatedAt.AddHours(1));

        var retryTimestamp = baseUpdatedAt.AddSeconds(-1);
        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = _ => [new RemoteSyncPushResult("order-1", false, "conflict")],
            PullSinceHandler = sinceUtc => sinceUtc == retryTimestamp ? [remoteOrder] : [],
        };
        outbox.Seed(Entry(localOrder, sequenceNumber: 1));
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Reconciled.Should().Be(1);
        result.Failed.Should().Be(0);
        // Proves the gateway routed through the escalation path specifically, not just "resolved
        // somehow" — only EscalateToConflictReview sets this status.
        var saved = await localStore.GetAsync("order-1");
        saved!.Status.Should().Be(FieldWorkStatus.ConflictReview);
    }

    [Fact]
    public async Task SyncAsync_PushFailsBaseSnapshotExistsButRemoteHasNoMatchingOrder_FallsBackToFailure()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var cursorStore = new FakeLocalSyncCursorStore(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var baseUpdatedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var baseOrder = Order("order-1", FieldWorkStatus.OnSite, assignedTo: "crew-1", updatedAt: baseUpdatedAt);
        await localStore.SaveBaseSnapshotAsync(baseOrder);

        var localOrder = Order("order-1", FieldWorkStatus.Completed, assignedTo: "crew-1", updatedAt: baseUpdatedAt);

        var client = new FakeFieldServiceSyncClient
        {
            PushHandler = _ => [new RemoteSyncPushResult("order-1", false, "conflict")],
            PullSinceHandler = _ => [], // remote hasn't actually changed since baseOrder.UpdatedAt
        };
        outbox.Seed(Entry(localOrder, sequenceNumber: 1));
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Failed.Should().Be(1);
        result.Reconciled.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Be("order-1: conflict");
    }

    [Fact]
    public async Task SyncAsync_DeltaPullReturnsRemoteOrders_UpsertsAndSavesBaseSnapshotForEach()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var sinceUtc = new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        var cursorStore = new FakeLocalSyncCursorStore(sinceUtc);

        var remote1 = Order("order-1", FieldWorkStatus.Pending);
        var remote2 = Order("order-2", FieldWorkStatus.Dispatched);
        var client = new FakeFieldServiceSyncClient
        {
            PullSinceHandler = s => s == sinceUtc ? [remote1, remote2] : [],
        };
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        var result = await sut.SyncAsync();

        result.Pulled.Should().Be(2);
        (await localStore.GetAsync("order-1")).Should().NotBeNull();
        (await localStore.GetAsync("order-2")).Should().NotBeNull();
        (await localStore.GetBaseSnapshotAsync("order-1")).Should().NotBeNull();
        (await localStore.GetBaseSnapshotAsync("order-2")).Should().NotBeNull();
    }

    [Fact]
    public async Task SyncAsync_DeltaPullStep_IsCalledWithExactlyTheCursorsLastSyncedValue()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var localStore = new FakeLocalServiceOrderStore();
        var outbox = new FakeLocalActionOutbox();
        var sinceUtc = new DateTime(2025, 11, 20, 3, 4, 5, DateTimeKind.Utc);
        var cursorStore = new FakeLocalSyncCursorStore(sinceUtc);
        var client = new FakeFieldServiceSyncClient();
        var sut = Sut(localStore, outbox, cursorStore, client, timeProvider);

        await sut.SyncAsync();

        client.PullSinceCalls.Should().ContainSingle().Which.Should().Be(sinceUtc);
    }
}
