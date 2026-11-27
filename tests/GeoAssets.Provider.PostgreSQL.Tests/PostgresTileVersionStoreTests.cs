using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Npgsql;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// XD01-458 — <see cref="PostgresTileVersionStore"/> on the collection's migrated PostGIS instance (migration
/// <c>AddTileVersions</c>): the epoch is chosen once and shared; generations start at 0 and advance by one, atomically with
/// the flush count; every committed change reaches a listener on <c>LISTEN</c> — whole, with its regions merged into one when
/// the payload would be too large, or marked incomplete when even that is; and a lost listening connection ends the
/// subscription with an exception. Each test uses its own scopes; the epoch and flush count are shared, so only their
/// changes are asserted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTileVersionStoreTests(PostgresContainerFixture fixture)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private PostgresTileVersionStore Store() => new(fixture.ConnectionString);

    [Fact]
    public async Task TheEpoch_IsChosenOnce_AndAScopeNeverAdvancedIsAtZero()
    {
        var scope = Guid.NewGuid();

        var first = await Store().LoadAsync([scope], CancellationToken.None);
        var second = await Store().LoadAsync([scope], CancellationToken.None);

        first.Epoch.Should().MatchRegex("^[0-9a-f]{8}$");
        second.Epoch.Should().Be(first.Epoch, "every replica over the database shares it");
        first.Generations.Should().Equal(new Dictionary<Guid, long> { [scope] = 0 });
    }

    [Fact]
    public async Task Advance_BumpsEachScopeByOne_AndTheFlushCountWhenAsked_Durably()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var store = Store();
        var before = await store.LoadAsync([a, b], CancellationToken.None);

        var first = await store.AdvanceAsync("test", [a, b, a], flush: false, [], CancellationToken.None);
        var second = await store.AdvanceAsync("test", [a], flush: true, [], CancellationToken.None);
        var after = await Store().LoadAsync([a, b], CancellationToken.None);

        first.Generations.Should().Equal(new Dictionary<Guid, long> { [a] = 1, [b] = 1 });
        first.Flushes.Should().BeNull();
        second.Generations.Should().Equal(new Dictionary<Guid, long> { [a] = 2 });
        second.Flushes.Should().BeGreaterThan(before.Flushes);
        after.Generations.Should().Equal(new Dictionary<Guid, long> { [a] = 2, [b] = 1 }, "a new store — a restarted replica — reads them back");
        after.Flushes.Should().Be(second.Flushes!.Value);
    }

    [Fact]
    public async Task ACommittedChange_ReachesAListener_Whole()
    {
        var scope = Guid.NewGuid();
        var region = new DirtyTileRegion(new BoundingBox(-38.5005, -13.0005, -38.4995, -12.9995), 3, 18);
        await using var listener = await ListenerAsync();

        var change = await Store().AdvanceAsync("writer-1", [scope], flush: true, [region], CancellationToken.None);
        var heard = await listener.NextAsync("writer-1");

        heard.IsComplete.Should().BeTrue();
        heard.Generations.Should().Equal(new Dictionary<Guid, long> { [scope] = 1 });
        heard.Flushes.Should().Be(change.Flushes);
        heard.Regions.Should().Equal(region);
    }

    [Fact]
    public async Task ATooLargeChange_IsHeardWithItsRegionsMerged_OrIncomplete()
    {
        var scope = Guid.NewGuid();
        var regions = Enumerable.Range(0, 400)
            .Select(i => new DirtyTileRegion(new BoundingBox(-40 + i * 0.01, -13, -40 + i * 0.01 + 0.001, -12.999), 0, 10 + i % 5))
            .ToList();
        var scopes = Enumerable.Range(0, 300).Select(_ => Guid.NewGuid()).ToList();
        await using var listener = await ListenerAsync();

        await Store().AdvanceAsync("writer-merged", [scope], flush: false, regions, CancellationToken.None);
        await Store().AdvanceAsync("writer-incomplete", scopes, flush: false, [], CancellationToken.None);
        var merged = await listener.NextAsync("writer-merged");
        var incomplete = await listener.NextAsync("writer-incomplete");

        merged.IsComplete.Should().BeTrue();
        merged.Generations.Should().ContainKey(scope);
        merged.Regions.Should().ContainSingle().Which.Should().Be(
            new DirtyTileRegion(new BoundingBox(-40, -13, -40 + 399 * 0.01 + 0.001, -12.999), 0, 14), "the union, at every zoom the regions named");
        incomplete.IsComplete.Should().BeFalse("300 scopes do not fit in a notification: the listener re-reads instead");
    }

    [Fact]
    public async Task ALostListeningConnection_EndsTheSubscriptionWithAnException()
    {
        await using var listener = await ListenerAsync();

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var terminate = new NpgsqlCommand(
                $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE query = 'LISTEN {PostgresTileVersionStore.Channel}' AND pid <> pg_backend_pid()",
                connection);
            await terminate.ExecuteNonQueryAsync();
        }

        var ended = await Task.WhenAny(listener.Listening, Task.Delay(Bound));
        ended.Should().BeSameAs(listener.Listening);
        listener.Listening.IsFaulted.Should().BeTrue("the subscription failed rather than being cancelled");
    }

    [Fact]
    public async Task TheAppRole_MayReadAndAdvanceTheVersions()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var check = new NpgsqlCommand(
            """
            SELECT has_table_privilege('geoassets_app', 'tile_generations', 'SELECT, INSERT, UPDATE')
               AND has_table_privilege('geoassets_app', 'tile_version_state', 'SELECT, INSERT, UPDATE')
            """,
            connection);

        ((bool)(await check.ExecuteScalarAsync())!).Should().BeTrue();
    }

    private async Task<Listener> ListenerAsync()
    {
        var listener = new Listener(Store());
        await listener.Subscribed.WaitAsync(Bound);
        return listener;
    }

    /// <summary>One subscription, recording every change heard until disposed.</summary>
    private sealed class Listener : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly TaskCompletionSource _subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<TileVersionChange> _heard = new();

        public Listener(PostgresTileVersionStore store)
        {
            Listening = store.ListenAsync(
                _ =>
                {
                    _subscribed.TrySetResult();
                    return Task.CompletedTask;
                },
                (change, _) =>
                {
                    _heard.Enqueue(change);
                    return Task.CompletedTask;
                },
                _stop.Token);
        }

        public Task Listening { get; }

        public Task Subscribed => _subscribed.Task;

        public async Task<TileVersionChange> NextAsync(string origin)
        {
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < Bound)
            {
                var match = _heard.FirstOrDefault(c => string.Equals(c.Origin, origin, StringComparison.Ordinal));
                if (match is not null) return match;
                await Task.Delay(20);
            }

            throw new TimeoutException($"No change from '{origin}' was heard within {Bound}.");
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await Task.WhenAny(Listening);
            _stop.Dispose();
        }
    }
}
