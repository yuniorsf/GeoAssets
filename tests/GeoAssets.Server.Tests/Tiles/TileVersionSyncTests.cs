using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-458 — <see cref="TileVersionSync"/>, <see cref="TileVersionListener"/> and the shared path of
/// <see cref="TileInvalidation"/> over a <see cref="FakeTileVersionStore"/>: startup reads the shared epoch and flushes once;
/// an organization's generation is read once; a write publishes only the values the store answers; a peer's change evicts
/// its dirty tiles before its versions are adopted (this process's own are skipped); a gap — an incomplete change or a
/// new subscription — empties the cache and re-reads the versions; a refused change flushes every replica once the store
/// is back; and a lost subscription is retried after the <see cref="TimeProvider"/>'s delay.
/// </summary>
public sealed class TileVersionSyncTests
{
    private const string Style = "0123456789ab";
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000458");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000458");
    private static readonly BoundingBox Change = new(-38.5005, -13.0005, -38.4995, -12.9995);
    private static readonly TileAddress Dirty = TileAddress.Containing(-38.5, -13.0, 12);
    private static readonly TileAddress Clean = TileAddress.Containing(-40.5, -15.0, 12);

    // ── Startup & reads ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ready_AdoptsTheSharedEpochAndGeneration_AndFlushesOnce()
    {
        var store = new FakeTileVersionStore();
        store.Set(Guid.Empty, 4);
        var (sync, versions, _) = Create(store);

        await sync.EnsureReadyAsync(CancellationToken.None);
        await sync.EnsureReadyAsync(CancellationToken.None);

        versions.Epoch.Should().Be("feed0458");
        versions.Flushes.Should().Be(1, "a starting replica flushes every replica's cache versions, once");
        versions.GenerationOf(OrgA).Should().Be(4);
        store.Advances.Should().ContainSingle(a => a.Flush && a.Scopes.Count == 0);
    }

    [Fact]
    public async Task AFailedStart_IsTriedAgain()
    {
        var store = new FakeTileVersionStore { Refuse = true };
        var (sync, versions, _) = Create(store);

        var act = () => sync.EnsureReadyAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        store.Refuse = false;
        await sync.EnsureReadyAsync(CancellationToken.None);

        versions.Flushes.Should().Be(1);
    }

    [Fact]
    public async Task AnOrganizationsGeneration_IsReadOnce()
    {
        var store = new FakeTileVersionStore();
        store.Set(OrgA, 6);
        var (sync, versions, _) = Create(store);

        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await sync.EnsureCurrentAsync(Guid.Empty, CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(6);
        store.Loads.Count(l => l.Contains(OrgA)).Should().Be(1);
    }

    // ── Own writes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWrite_PublishesTheStoresGenerations_ForItsOrganizationAndGrantees_AfterEvicting()
    {
        var store = new FakeTileVersionStore();
        store.Set(OrgA, 6);
        var grants = new Grants();
        grants.ByGrantee[OrgB] = [new OrganizationGrant { GranteeOrganizationId = OrgB, ResourceOrganizationId = OrgA, AllowedActions = ["features:read"] }];
        var (sync, versions, cache) = Create(store);
        var invalidation = Invalidation(cache, versions, sync, grants);
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await SeedAsync(cache, versions);
        var generation = versions.Generation;

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        store.Advances.Last().Scopes.Should().BeEquivalentTo([OrgA, OrgB]);
        store.Advances.Last().Regions.Should().Equal(new DirtyTileRegion(Change));
        versions.GenerationOf(OrgA).Should().Be(7, "the store's answer, not a local count");
        versions.GenerationOf(OrgB).Should().Be(1);
        versions.Generation.Should().BeGreaterThan(generation);
        (await cache.GetAsync(Key(Dirty, versions), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(Clean, versions), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task AWriteTooLargeToEvict_FlushesThroughTheStore_WithoutRegions()
    {
        var store = new FakeTileVersionStore();
        var (sync, versions, cache) = Create(store, maxEvicted: 100);
        var invalidation = Invalidation(cache, versions, sync, new Grants(), maxEvicted: 100);
        await sync.EnsureReadyAsync(CancellationToken.None);

        await invalidation.InvalidateAsync([new DirtyTileRegion(new BoundingBox(-47.5, -24, -45, -22))], OrgA, CancellationToken.None);

        store.Advances.Last().Flush.Should().BeTrue();
        store.Advances.Last().Regions.Should().BeEmpty("the flush count tells the peers; no tile needs naming");
        versions.Flushes.Should().Be(2);
    }

    [Fact]
    public async Task ARefusedWrite_EvictsHere_AndFlushesEveryReplicaOnceTheStoreIsBack()
    {
        var store = new FakeTileVersionStore();
        var (sync, versions, cache) = Create(store);
        var invalidation = Invalidation(cache, versions, sync, new Grants());
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await SeedAsync(cache, versions);
        var published = versions.PublishedVersion(Style, OrgA);
        store.Refuse = true;

        var act = () => invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sync.RecoveryPending.Should().BeTrue();
        (await cache.GetAsync(Key(Dirty, versions), CancellationToken.None)).Should().BeNull("this replica's stale tiles go at once");
        versions.PublishedVersion(Style, OrgA).Should().Be(published, "no {v} the store does not hold is published");

        await FluentActions.Awaiting(() => sync.RecoverAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        sync.RecoveryPending.Should().BeTrue("the store still refuses it");
        store.Refuse = false;
        await sync.RecoverAsync(CancellationToken.None);

        sync.RecoveryPending.Should().BeFalse();
        store.Advances.Last().Flush.Should().BeTrue();
        versions.PublishedVersion(Style, OrgA).Should().NotBe(published);
    }

    // ── Peers ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task APeersChange_EvictsItsDirtyTiles_AndAdvancesTheScopes_WhileThisProcesssOwnAreSkipped()
    {
        var store = new FakeTileVersionStore();
        var (sync, versions, cache) = Create(store);
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await SeedAsync(cache, versions);

        await sync.ApplyAsync(new TileVersionChange(sync.Origin, new Dictionary<Guid, long> { [OrgA] = 9 }, null, [new DirtyTileRegion(Change)]), CancellationToken.None);
        cache.Count.Should().Be(2, "this process applied its own change when it made it");
        versions.GenerationOf(OrgA).Should().Be(0);

        await sync.ApplyAsync(new TileVersionChange("peer", new Dictionary<Guid, long> { [OrgA] = 3 }, null, [new DirtyTileRegion(Change)]), CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(3);
        versions.GenerationOf(OrgB).Should().Be(0);
        (await cache.GetAsync(Key(Dirty, versions), CancellationToken.None)).Should().BeNull();
        (await cache.GetAsync(Key(Clean, versions), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task APeersOlderChange_NeverMovesAScopeBackwards()
    {
        var store = new FakeTileVersionStore();
        var (sync, versions, _) = Create(store);
        await sync.EnsureReadyAsync(CancellationToken.None);

        await sync.ApplyAsync(new TileVersionChange("peer", new Dictionary<Guid, long> { [OrgA] = 5 }, 7, []), CancellationToken.None);
        await sync.ApplyAsync(new TileVersionChange("peer", new Dictionary<Guid, long> { [OrgA] = 4 }, 6, []), CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(5);
        versions.Flushes.Should().Be(7);
    }

    [Fact]
    public async Task AnIncompleteChange_EmptiesTheCache_AndReReadsTheVersions()
    {
        var store = new FakeTileVersionStore();
        var (sync, versions, cache) = Create(store);
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await SeedAsync(cache, versions);
        store.Set(OrgA, 12);

        await sync.ApplyAsync(new TileVersionChange("peer", new Dictionary<Guid, long>(), null, [], IsComplete: false), CancellationToken.None);

        cache.Count.Should().Be(0);
        versions.GenerationOf(OrgA).Should().Be(12);
    }

    // ── Listener ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheListener_RetriesALostSubscriptionAfterItsDelay_AndTreatsTheGapAsUnknown()
    {
        var reconnects = 0L;
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Name, TileMetrics.ReconnectsName, StringComparison.Ordinal)) l.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref reconnects, value));
        meter.Start();

        var time = new FakeTimeProvider();
        var store = new FakeTileVersionStore { RefuseSubscriptions = 1 };
        var (sync, versions, cache) = Create(store);
        using var listener = new TileVersionListener(store, sync, TileVersionSyncOptions.Default, time, NullLogger<TileVersionListener>.Instance);

        await listener.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => store.Subscriptions == 1);
        await Task.Delay(50);
        store.Subscriptions.Should().Be(1, "nothing is retried before the delay");
        (await AdvancingAsync(time, store.WaitSubscribedAsync())).Should().BeTrue();

        store.Set(OrgA, 5);
        await sync.EnsureCurrentAsync(OrgA, CancellationToken.None);
        await SeedAsync(cache, versions);
        store.Deliver(new TileVersionChange("peer", new Dictionary<Guid, long> { [OrgA] = 6 }, null, [new DirtyTileRegion(Change)]));
        (await store.WaitAppliedAsync()).Should().BeTrue();
        versions.GenerationOf(OrgA).Should().Be(6);
        cache.Count.Should().Be(1, "the change's dirty tile was evicted");

        store.Set(OrgA, 8);
        store.Drop();
        (await AdvancingAsync(time, store.WaitSubscribedAsync())).Should().BeTrue();

        versions.GenerationOf(OrgA).Should().Be(8, "a new subscription re-reads every known scope: what it missed is unknown");
        cache.Count.Should().Be(0, "and empties the cache");
        store.Subscriptions.Should().Be(3);
        Interlocked.Read(ref reconnects).Should().BeGreaterThanOrEqualTo(2);

        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheListener_RetriesARefusedChangesRecovery_EveryInterval()
    {
        var time = new FakeTimeProvider();
        var store = new FakeTileVersionStore();
        var (sync, versions, cache) = Create(store);
        var invalidation = Invalidation(cache, versions, sync, new Grants());
        await sync.EnsureReadyAsync(CancellationToken.None);
        store.Refuse = true;
        await FluentActions.Awaiting(() => invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        store.Refuse = false;
        using var listener = new TileVersionListener(store, sync, TileVersionSyncOptions.Default, time, NullLogger<TileVersionListener>.Instance);

        await listener.StartAsync(CancellationToken.None);
        (await store.WaitSubscribedAsync()).Should().BeTrue();
        sync.RecoveryPending.Should().BeTrue();
        await AdvancingAsync(time, WaitUntilAsync(() => !sync.RecoveryPending));

        versions.Flushes.Should().Be(2, "the startup flush and the recovery's");
        await listener.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void TheOptions_RefuseNonPositiveDelays()
    {
        var act = () => (TileVersionSyncOptions.Default with { ReconnectDelay = TimeSpan.Zero }).Validated();
        var below = () => (TileVersionSyncOptions.Default with { MaxReconnectDelay = TimeSpan.FromMilliseconds(1) }).Validated();
        var recovery = () => (TileVersionSyncOptions.Default with { RecoveryInterval = TimeSpan.Zero }).Validated();

        act.Should().Throw<ArgumentOutOfRangeException>();
        below.Should().Throw<ArgumentOutOfRangeException>();
        recovery.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
            await Task.Delay(10);

        condition().Should().BeTrue();
    }

    /// <summary>Awaits <paramref name="waiting"/>, advancing <paramref name="time"/> a second at a time meanwhile (a delay registered late still elapses).</summary>
    private static async Task<T> AdvancingAsync<T>(FakeTimeProvider time, Task<T> waiting)
    {
        while (!waiting.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(waiting, Task.Delay(10));
        }

        return await waiting;
    }

    private static async Task AdvancingAsync(FakeTimeProvider time, Task waiting)
    {
        while (!waiting.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(waiting, Task.Delay(10));
        }

        await waiting;
    }

    private static async Task SeedAsync(MemoryTileCache cache, TileVersions versions)
    {
        await cache.SetAsync(Key(Dirty, versions), Payload(), CancellationToken.None);
        await cache.SetAsync(Key(Clean, versions), Payload(), CancellationToken.None);
    }

    private static TileCacheKey Key(TileAddress tile, TileVersions versions) =>
        new(TileCacheScope.Create("org-a", []), "assets", versions.CacheVersion(Style), tile, TileFormat.Mvt);

    private static TilePayload Payload() => new(new byte[] { 1, 2, 3 }, TileFormats.MapboxVectorTileContentType);

    private static (TileVersionSync Sync, TileVersions Versions, MemoryTileCache Cache) Create(
        FakeTileVersionStore store, int maxEvicted = TileServingOptions.DefaultMaxEvictedTilesPerWrite)
    {
        var versions = new TileVersions("cafe0458");
        var cache = new MemoryTileCache();
        var options = TileServingOptions.Default with { MaxEvictedTilesPerWrite = maxEvicted };
        return (new TileVersionSync(store, versions, cache, options, NullLogger<TileVersionSync>.Instance), versions, cache);
    }

    private static TileInvalidation Invalidation(
        MemoryTileCache cache, TileVersions versions, TileVersionSync sync, IOrganizationGrantRepository grants, int maxEvicted = TileServingOptions.DefaultMaxEvictedTilesPerWrite)
    {
        var services = new ServiceCollection().AddSingleton(grants).BuildServiceProvider();
        var options = TileServingOptions.Default with { MaxEvictedTilesPerWrite = maxEvicted };
        return new TileInvalidation(cache, versions, options, services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, sync);
    }
}
