using FluentAssertions;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="ScopedRoleAssignmentSyncWorker"/> (XD01-328): each entry replaces the (user, organization) row it is about, a
/// tombstone deletes it, and the cursor advances with the batch — the twin of <see cref="OrganizationGrantSyncWorkerTests"/>.
/// </summary>
public sealed class ScopedRoleAssignmentSyncWorkerTests
{
    private static readonly DateTimeOffset FixedStartUtc = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = new("aaaaaaaa-0000-4000-8000-000000000328");
    private static readonly Guid Coelba = new("c0c0c0c0-0000-4000-8000-000000000328");
    private static readonly Guid Third = new("d0d0d0d0-0000-4000-8000-000000000328");

    private sealed class FakeSyncClient : IScopedRoleAssignmentSyncClient
    {
        private readonly List<ScopedRoleAssignmentSyncEntry> _entries = [];

        public List<long> ObservedAfterSequences { get; } = [];

        public void Add(ScopedRoleAssignmentSyncEntry entry) => _entries.Add(entry);

        public Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default)
        {
            ObservedAfterSequences.Add(afterSequence);
            return Task.FromResult<IReadOnlyList<ScopedRoleAssignmentSyncEntry>>(
                [.. _entries.Where(e => e.SequenceNumber > afterSequence).OrderBy(e => e.SequenceNumber).Take(take)]);
        }
    }

    private static ScopedRoleAssignmentSyncEntry Entry(long sequence, Guid organization, bool active, params string[] codes) => new()
    {
        SequenceNumber = sequence,
        UserId = UserId,
        ExternalObjectId = "oid-ada",
        ResourceOrganizationId = organization,
        RoleNames = active ? ["Contractor C1"] : [],
        PermissionCodes = active ? codes : [],
        IsActive = active,
    };

    private static ScopedRoleAssignmentSyncWorker Worker(
        ServiceProvider services, FakeSyncClient client, OrganizationGrantSyncOptions? options = null) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        client,
        new FakeTimeProvider(FixedStartUtc),
        Options.Create(options ?? new OrganizationGrantSyncOptions()),
        NullLogger<ScopedRoleAssignmentSyncWorker>.Instance);

    private static ServiceProvider Services(SqliteFixture fixture) =>
        new ServiceCollection().AddScoped(_ => fixture.NewContext()).BuildServiceProvider();

    [Fact]
    public async Task FirstRun_UpsertsTheRowAndCreatesTheCursor()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        client.Add(Entry(1, Coelba, active: true, "serviceorders:view"));
        await using var services = Services(fixture);
        using var worker = Worker(services, client);

        (await worker.SyncNextBatchAsync()).Should().Be(1);

        client.ObservedAfterSequences.Should().Equal(0);
        var row = await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().SingleAsync();
        row.UserId.Should().Be(UserId);
        row.ResourceOrganizationId.Should().Be(Coelba);
        row.ExternalObjectId.Should().Be("oid-ada");
        row.RoleNamesJson.Should().Be("""["Contractor C1"]""");
        row.PermissionCodesJson.Should().Be("""["serviceorders:view"]""");
        row.SyncedAtUtc.Should().Be(FixedStartUtc.UtcDateTime);
        (await fixture.Context.ScopedRoleAssignmentSyncCursor.AsNoTracking().SingleAsync()).LastSequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task ALaterEntry_ReplacesTheRow_AndATombstoneDeletesIt()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        client.Add(Entry(1, Coelba, active: true, "serviceorders:view"));
        await using var services = Services(fixture);
        using var worker = Worker(services, client);
        await worker.SyncNextBatchAsync();

        client.Add(Entry(2, Coelba, active: true, "serviceorders:view", "serviceorders:execute"));
        await worker.SyncNextBatchAsync();
        (await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().SingleAsync()).PermissionCodesJson
            .Should().Be("""["serviceorders:view","serviceorders:execute"]""");

        client.Add(Entry(3, Coelba, active: false));
        await worker.SyncNextBatchAsync();

        (await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().ToListAsync()).Should().BeEmpty("a revocation arrives as a tombstone");
        (await fixture.Context.ScopedRoleAssignmentSyncCursor.AsNoTracking().SingleAsync()).LastSequenceNumber.Should().Be(3);
        client.ObservedAfterSequences.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task ATombstoneForAPairNeverMirrored_IsANoOp_ButStillAdvancesTheCursor()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        client.Add(Entry(4, Third, active: false));
        await using var services = Services(fixture);
        using var worker = Worker(services, client);

        (await worker.SyncNextBatchAsync()).Should().Be(1);

        (await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().ToListAsync()).Should().BeEmpty();
        (await fixture.Context.ScopedRoleAssignmentSyncCursor.AsNoTracking().SingleAsync()).LastSequenceNumber.Should().Be(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SeveralEntriesForOnePairInOneBatch_TheLastOneWins(bool lastIsActive)
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        client.Add(Entry(1, Coelba, active: !lastIsActive, "serviceorders:view"));
        client.Add(Entry(2, Coelba, active: lastIsActive, "serviceorders:execute"));
        client.Add(Entry(3, Third, active: true, "serviceorders:view"));
        await using var services = Services(fixture);
        using var worker = Worker(services, client);

        (await worker.SyncNextBatchAsync()).Should().Be(3);

        var rows = await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().OrderBy(r => r.ResourceOrganizationId).ToListAsync();
        if (lastIsActive)
        {
            rows.Select(r => r.ResourceOrganizationId).Should().Equal(Coelba, Third);
            rows[0].PermissionCodesJson.Should().Be("""["serviceorders:execute"]""");
        }
        else
        {
            rows.Select(r => r.ResourceOrganizationId).Should().Equal(Third);
        }
    }

    [Fact]
    public async Task NoNewEntries_ReturnsZero_AndCreatesNoCursor()
    {
        using var fixture = new SqliteFixture();
        await using var services = Services(fixture);
        using var worker = Worker(services, new FakeSyncClient());

        (await worker.SyncNextBatchAsync()).Should().Be(0);

        (await fixture.Context.ScopedRoleAssignmentSyncCursor.AsNoTracking().ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ABatchLargerThanTheBatchSize_IsAppliedAcrossCycles()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        client.Add(Entry(1, Coelba, active: true, "serviceorders:view"));
        client.Add(Entry(2, Third, active: true, "serviceorders:view"));
        await using var services = Services(fixture);
        using var worker = Worker(services, client, new OrganizationGrantSyncOptions { BatchSize = 1 });

        (await worker.SyncNextBatchAsync()).Should().Be(1);
        (await worker.SyncNextBatchAsync()).Should().Be(1);
        (await worker.SyncNextBatchAsync()).Should().Be(0);

        (await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().CountAsync()).Should().Be(2);
        client.ObservedAfterSequences.Should().Equal(0, 1, 2);
    }

    /// <summary>A client whose first call fails, then serves one entry — guarded by a lock, since the worker polls from
    /// another thread.</summary>
    private sealed class FlakyThenServingClient : IScopedRoleAssignmentSyncClient
    {
        private readonly Lock _gate = new();
        private readonly List<long> _observed = [];
        private int _calls;

        public IReadOnlyList<long> Observed
        {
            get
            {
                lock (_gate) return [.. _observed];
            }
        }

        public Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default)
        {
            lock (_gate)
            {
                _observed.Add(afterSequence);
                if (++_calls == 1) throw new HttpRequestException("core unreachable");
                return Task.FromResult<IReadOnlyList<ScopedRoleAssignmentSyncEntry>>(
                    afterSequence == 0 ? [Entry(1, Coelba, active: true, "serviceorders:view")] : []);
            }
        }
    }

    [Fact]
    public async Task BackgroundLoop_RetriesAfterAFailure_ThenAppliesTheEntry()
    {
        using var fixture = new SqliteFixture();
        var client = new FlakyThenServingClient();
        var pollInterval = TimeSpan.FromSeconds(5);
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var services = Services(fixture);
        using var worker = new ScopedRoleAssignmentSyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions { PollInterval = pollInterval }),
            NullLogger<ScopedRoleAssignmentSyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        // Asked for entries after sequence 1: the entry was applied and the cursor saved, after the failed first poll.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!client.Observed.Contains(1))
        {
            timeProvider.Advance(pollInterval);
            await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
        }

        await worker.StopAsync(CancellationToken.None);

        client.Observed.Take(2).Should().Equal(0, 0);
        (await fixture.Context.ScopedRoleAssignmentMirror.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Disabled_TheBackgroundLoopNeverPolls()
    {
        using var fixture = new SqliteFixture();
        var client = new FakeSyncClient();
        await using var services = Services(fixture);
        using var worker = Worker(services, client, new OrganizationGrantSyncOptions { EnableSyncWorker = false });

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);

        client.ObservedAfterSequences.Should().BeEmpty();
    }
}
