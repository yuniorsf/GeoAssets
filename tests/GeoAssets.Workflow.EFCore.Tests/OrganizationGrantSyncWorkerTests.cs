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

    private sealed class FakeSyncClient : IOrganizationGrantSyncClient
    {
        public List<OrganizationGrantSyncEntry> Entries { get; } = [];
        public List<long> ObservedAfterSequences { get; } = [];

        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
            long afterSequence, int take, CancellationToken ct = default)
        {
            ObservedAfterSequences.Add(afterSequence);
            var batch = Entries
                .Where(e => e.SequenceNumber > afterSequence)
                .OrderBy(e => e.SequenceNumber)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<OrganizationGrantSyncEntry>>(batch);
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
        client.Entries.Add(new OrganizationGrantSyncEntry
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
        client.Entries.Add(new OrganizationGrantSyncEntry
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
        client.Entries.Add(new OrganizationGrantSyncEntry
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
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        await using var serviceProvider = BuildScopeProvider(fixture);
        using var worker = new OrganizationGrantSyncWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            client,
            timeProvider,
            Options.Create(new OrganizationGrantSyncOptions { PollInterval = TimeSpan.FromSeconds(5) }),
            NullLogger<OrganizationGrantSyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        for (var step = 0; step < 5 && !fixture.Context.OrganizationGrantMirror.Any(); step++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        client.Entries.Add(new OrganizationGrantSyncEntry
        {
            SequenceNumber = 1,
            GrantId = Guid.NewGuid(),
            GranteeOrganizationId = Guid.NewGuid(),
            ResourceOrganizationId = Guid.NewGuid(),
            IsActive = true,
        });

        for (var step = 0; step < 20 && !fixture.Context.OrganizationGrantMirror.Any(); step++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        fixture.Context.OrganizationGrantMirror.Count().Should().Be(1);

        await worker.StopAsync(CancellationToken.None);
    }
}
