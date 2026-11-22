using FluentAssertions;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public class OrganizationGrantSyncWorkerTests
{
    private static readonly DateTimeOffset FixedStartUtc =
        DateTimeOffset.Parse("2026-11-11T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Thread-safe: the background-loop test adds an entry from the test thread while the worker
    /// polls from a thread-pool thread, so both collections are guarded by one lock.
    /// </summary>
    private sealed class FakeSyncClient : IOrganizationGrantSyncClient
    {
        private readonly Lock _gate = new();
        private readonly List<OrganizationGrantSyncEntry> _entries = [];
        private readonly List<long> _observedAfterSequences = [];

        public IReadOnlyList<long> ObservedAfterSequences
        {
            get
            {
                lock (_gate)
                {
                    return [.. _observedAfterSequences];
                }
            }
        }

        public IReadOnlyList<OrganizationGrantSyncEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        public void Add(OrganizationGrantSyncEntry entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }

        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
            long afterSequence, int take, CancellationToken ct = default)
        {
            lock (_gate)
            {
                _observedAfterSequences.Add(afterSequence);
                var batch = _entries
                    .Where(e => e.SequenceNumber > afterSequence)
                    .OrderBy(e => e.SequenceNumber)
                    .Take(take)
                    .ToList();
                return Task.FromResult<IReadOnlyList<OrganizationGrantSyncEntry>>(batch);
            }
        }
    }

    /// <summary>
    /// Advances fake time one poll interval at a time until <paramref name="condition"/> holds.
    /// The worker runs on another thread, so a fixed number of advances can all fire before it
    /// has registered its next delay; this keeps advancing until the worker has demonstrably
    /// made progress. The real-time pause and timeout only pace and bound the wait — the
    /// assertion never depends on how long anything took.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, TimeSpan pollInterval, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition())
        {
            timeProvider.Advance(pollInterval);
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }
    }

    private static ServiceProvider BuildScopeProvider(SqliteFixture fixture) =>
        new ServiceCollection()
            .AddScoped(_ => fixture.NewContext())
            .BuildServiceProvider();

    [Fact]
    public async Task SyncNextBatchAsync_FirstRunWithNoCursor_UpsertsMirrorRowsAndCreatesCursor()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        var granteeOrg = Guid.NewGuid();
        var resourceOrg = Guid.NewGuid();
        client.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 1,
            GrantId = Guid.NewGuid(),
            GranteeOrganizationId = granteeOrg,
            ResourceOrganizationId = resourceOrg,
            ResourceType = "ServiceOrder",
            IsActive = true,
        });

        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions()),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        var applied = await worker.SyncNextBatchAsync();

        applied.Should().Be(1);
        client.ObservedAfterSequences.Should().ContainSingle().Which.Should().Be(0);

        var expectedGrantId = client.Entries[0].GrantId;
        var mirror = fixture.Context.OrganizationGrantMirror.Single(m => m.GrantId == expectedGrantId);
        mirror.GranteeOrganizationId.Should().Be(granteeOrg);
        mirror.ResourceOrganizationId.Should().Be(resourceOrg);
        mirror.IsActive.Should().BeTrue();

        var cursor = fixture.Context.OrganizationGrantSyncCursor.Single(c => c.Id == 1);
        cursor.LastSequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task SyncNextBatchAsync_SecondRunAfterRevocation_UpsertsSameMirrorRowAndAdvancesCursor()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        var grantId = Guid.NewGuid();
        var granteeOrg = Guid.NewGuid();
        var resourceOrg = Guid.NewGuid();
        client.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 1,
            GrantId = grantId,
            GranteeOrganizationId = granteeOrg,
            ResourceOrganizationId = resourceOrg,
            IsActive = true,
        });

        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions()),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        await worker.SyncNextBatchAsync();

        // Revocation arrives as a second, later snapshot for the SAME grant.
        client.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 2,
            GrantId = grantId,
            GranteeOrganizationId = granteeOrg,
            ResourceOrganizationId = resourceOrg,
            IsActive = false,
        });

        var secondApplied = await worker.SyncNextBatchAsync();

        secondApplied.Should().Be(1);
        client.ObservedAfterSequences.Should().Equal(0, 1);

        var mirrorRows = fixture.Context.OrganizationGrantMirror.ToList();
        mirrorRows.Should().ContainSingle("the worker must upsert by GrantId, not insert a duplicate row");
        mirrorRows[0].IsActive.Should().BeFalse();

        var cursor = fixture.Context.OrganizationGrantSyncCursor.Single(c => c.Id == 1);
        cursor.LastSequenceNumber.Should().Be(2);
    }

    [Fact]
    public async Task SyncNextBatchAsync_MirrorsWhatTheCrossOrganizationRuleDecidesBy()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        var grantId = Guid.NewGuid();
        client.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 1,
            GrantId = grantId,
            GranteeOrganizationId = Guid.NewGuid(),
            ResourceOrganizationId = Guid.NewGuid(),
            ResourceType = "ServiceOrder",
            IsActive = true,
            AllowedActions = ["serviceorders:view", "serviceorders:execute"],
            RequiredRole = "Contractor C1",
            RequiresScopedRole = true,
            OrderTypeIds = ["cut"],
        });
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            new FakeTimeProvider(FixedStartUtc),
            Options.Create(new OrganizationGrantSyncOptions()),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        await worker.SyncNextBatchAsync();

        // XD01-328: the whole rule's input, not only the row-level exception's.
        var mirror = fixture.Context.OrganizationGrantMirror.Single(m => m.GrantId == grantId);
        mirror.AllowedActionsJson.Should().Be("""["serviceorders:view","serviceorders:execute"]""");
        mirror.RequiredRole.Should().Be("Contractor C1");
        mirror.RequiresScopedRole.Should().BeTrue();
        mirror.OrderTypeIdsJson.Should().Be("""["cut"]""");
    }

    [Fact]
    public async Task SyncNextBatchAsync_NoNewEntries_ReturnsZeroAndLeavesCursorUntouched()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions()),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        var applied = await worker.SyncNextBatchAsync();

        applied.Should().Be(0);
        fixture.Context.OrganizationGrantSyncCursor.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundLoopWithFakeTimeProvider_PollsAndAppliesNewEntry()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        var pollInterval = TimeSpan.FromSeconds(5);
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions { PollInterval = pollInterval }),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        // The worker has polled at least once with nothing to apply.
        await AdvanceUntilAsync(timeProvider, pollInterval, () => client.ObservedAfterSequences.Count > 0);

        client.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 1,
            GrantId = Guid.NewGuid(),
            GranteeOrganizationId = Guid.NewGuid(),
            ResourceOrganizationId = Guid.NewGuid(),
            IsActive = true,
        });

        // The worker reads its cursor from the database before every poll, so being asked for
        // entries after sequence 1 proves the entry was applied and saved.
        await AdvanceUntilAsync(timeProvider, pollInterval, () => client.ObservedAfterSequences.Contains(1));

        // Stop before reading: the test's DbContext shares the fixture's SQLite connection with
        // the worker's scoped context.
        await worker.StopAsync(CancellationToken.None);

        fixture.Context.OrganizationGrantMirror.Count().Should().Be(1);
    }
}
