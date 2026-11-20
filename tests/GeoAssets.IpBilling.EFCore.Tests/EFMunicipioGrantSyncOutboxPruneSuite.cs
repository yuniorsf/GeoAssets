using FluentAssertions;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// XD01-400: <see cref="EFMunicipioGrantSyncOutbox.PruneAsync"/> — finished requests are deleted, a município's latest request
/// (the one its status is derived from) and every unfinished request never are. One suite, run on SQLite and on PostgreSQL.
/// </summary>
public abstract class EFMunicipioGrantSyncOutboxPruneSuite : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedCutoff = Now - TimeSpan.FromDays(30);
    private static readonly DateTimeOffset FailedCutoff = Now - TimeSpan.FromDays(90);
    private static readonly DateTimeOffset OldCompleted = CompletedCutoff - TimeSpan.FromDays(1);
    private static readonly DateTimeOffset RecentCompleted = CompletedCutoff + TimeSpan.FromDays(1);
    private static readonly DateTimeOffset OldFailed = FailedCutoff - TimeSpan.FromDays(1);
    private static readonly DateTimeOffset RecentFailed = FailedCutoff + TimeSpan.FromDays(1);
    private static readonly Guid DistribuidoraOrg = new("d0d0d0d0-0000-4000-8000-000000000400");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000400");

    private IpBillingDbContext _db = null!;
    private readonly List<IDisposable> _owned = [];
    private EFMunicipioGrantSyncOutbox _outbox = null!;

    public async Task InitializeAsync()
    {
        _db = await CreateContextAsync();
        _outbox = new EFMunicipioGrantSyncOutbox(_db, new FakeTimeProvider(Now));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        foreach (var owned in _owned) owned.Dispose();
    }

    protected abstract Task<IpBillingDbContext> CreateContextAsync();

    protected T Own<T>(T disposable)
        where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    /// <summary>An empty outbox prunes to nothing, whatever the cutoffs.</summary>
    protected Task<int> PruneEmptyOutboxAsync() => _outbox.PruneAsync(Now, Now, 10);

    [Fact]
    public async Task Prune_DeletesAnOldCompletedRequest_WhenALaterOneExists()
    {
        var municipio = Guid.NewGuid();
        var old = await SeedAsync(municipio, completedAt: OldCompleted);
        var latest = await SeedAsync(municipio);

        var deleted = await _outbox.PruneAsync(CompletedCutoff, null, 100);

        deleted.Should().Be(1);
        (await IdsAsync()).Should().Equal(latest);
        (await IdsAsync()).Should().NotContain(old);
    }

    [Fact]
    public async Task Prune_KeepsAMunicipiosLatestRequest_EvenWhenOldAndCompleted()
    {
        var municipio = Guid.NewGuid();
        var only = await SeedAsync(municipio, completedAt: OldCompleted);
        var other = Guid.NewGuid();
        var olderOfOther = await SeedAsync(other, completedAt: OldCompleted);
        var latestOfOther = await SeedAsync(other, completedAt: OldCompleted);

        var deleted = await _outbox.PruneAsync(CompletedCutoff, null, 100);

        deleted.Should().Be(1);
        (await IdsAsync()).Should().BeEquivalentTo([only, latestOfOther]);
        (await IdsAsync()).Should().NotContain(olderOfOther);
    }

    [Fact]
    public async Task Prune_KeepsCompletedRequestsNewerThanTheCutoff()
    {
        var municipio = Guid.NewGuid();
        var recent = await SeedAsync(municipio, completedAt: RecentCompleted);
        var latest = await SeedAsync(municipio);

        var deleted = await _outbox.PruneAsync(CompletedCutoff, null, 100);

        deleted.Should().Be(0);
        (await IdsAsync()).Should().Equal(recent, latest);
    }

    [Fact]
    public async Task Prune_KeepsPendingLeasedAndFailedRequests_ThatAreNotTheLatest()
    {
        var municipio = Guid.NewGuid();
        var pending = await SeedAsync(municipio);
        var leased = await SeedAsync(municipio, leasedBy: "worker-1");
        var failed = await SeedAsync(municipio, failedAt: OldFailed);
        var latest = await SeedAsync(municipio);

        var deleted = await _outbox.PruneAsync(CompletedCutoff, null, 100);

        deleted.Should().Be(0);
        (await IdsAsync()).Should().Equal(pending, leased, failed, latest);
    }

    [Fact]
    public async Task Prune_KeepsPendingAndLeasedRequests_EvenWithAFailedCutoff()
    {
        var municipio = Guid.NewGuid();
        var pending = await SeedAsync(municipio);
        var leased = await SeedAsync(municipio, leasedBy: "worker-1");
        var latest = await SeedAsync(municipio);

        var deleted = await _outbox.PruneAsync(Now, Now, 100);

        deleted.Should().Be(0);
        (await IdsAsync()).Should().Equal(pending, leased, latest);
    }

    [Fact]
    public async Task Prune_DeletesAnOldFailedRequest_OnlyWithAFailedCutoff_AndNotWhenItIsTheLatest()
    {
        var municipio = Guid.NewGuid();
        var oldFailed = await SeedAsync(municipio, failedAt: OldFailed);
        var recentFailed = await SeedAsync(municipio, failedAt: RecentFailed);
        var other = Guid.NewGuid();
        var latestFailed = await SeedAsync(other, failedAt: OldFailed);

        (await _outbox.PruneAsync(CompletedCutoff, null, 100)).Should().Be(0);
        (await IdsAsync()).Should().HaveCount(3);

        (await _outbox.PruneAsync(CompletedCutoff, FailedCutoff, 100)).Should().Be(1);
        var remaining = await IdsAsync();
        remaining.Should().BeEquivalentTo([recentFailed, latestFailed]);
        remaining.Should().NotContain(oldFailed);
    }

    [Fact]
    public async Task Prune_RespectsTheBatchSize_DeletingTheOldestIdsFirst()
    {
        var eligible = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var municipio = Guid.NewGuid();
            eligible.Add(await SeedAsync(municipio, completedAt: OldCompleted));
            await SeedAsync(municipio);
        }

        var first = await _outbox.PruneAsync(CompletedCutoff, null, 3);

        first.Should().Be(3);
        (await IdsAsync()).Should().NotContain(eligible.Take(3));
        (await IdsAsync()).Should().Contain(eligible.Skip(3));

        var second = await _outbox.PruneAsync(CompletedCutoff, null, 3);

        second.Should().Be(2);
        (await IdsAsync()).Should().NotContain(eligible);
        (await _outbox.PruneAsync(CompletedCutoff, null, 3)).Should().Be(0);
    }

    [Fact]
    public async Task Prune_LeavesEveryMunicipiosStatusUnchanged()
    {
        var synced = await AddMunicipioAsync("3550308");
        await SeedAsync(synced.Id, completedAt: OldCompleted);
        await SeedAsync(synced.Id, completedAt: OldCompleted);
        var failed = await AddMunicipioAsync("3304557");
        await SeedAsync(failed.Id, completedAt: OldCompleted);
        await SeedAsync(failed.Id, failedAt: OldFailed, lastError: "InvalidOperationException: refused");
        var pending = await AddMunicipioAsync("3106200");
        await SeedAsync(pending.Id, completedAt: OldCompleted);
        await SeedAsync(pending.Id);
        await AddMunicipioAsync("4106902");

        var before = await StatusesAsync();
        var deleted = await _outbox.PruneAsync(CompletedCutoff, FailedCutoff, 100);
        var after = await StatusesAsync();

        deleted.Should().Be(3);
        after.Should().Equal(before);
        after.Select(s => s.State).Should().BeEquivalentTo(
            [MunicipioGrantSyncState.Synced, MunicipioGrantSyncState.Failed, MunicipioGrantSyncState.Pending, MunicipioGrantSyncState.None]);
        after.Single(s => string.Equals(s.IbgeCode, "3304557", StringComparison.Ordinal)).Error.Should().Be("InvalidOperationException: refused");
        after.Single(s => string.Equals(s.IbgeCode, "3550308", StringComparison.Ordinal)).SyncedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task Prune_RejectsANonPositiveBatchSize(int batchSize)
    {
        var act = () => _outbox.PruneAsync(CompletedCutoff, null, batchSize);

        return act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    private async Task<long> SeedAsync(
        Guid municipioId, DateTimeOffset? completedAt = null, DateTimeOffset? failedAt = null, string? leasedBy = null, string? lastError = null)
    {
        _outbox.Enqueue(new MunicipioGrantChange(municipioId, MunicipioGrantSyncKind.Sync, "3550308", DistribuidoraOrg, PrefeituraA), "requester-1");
        await _db.SaveChangesAsync();
        var id = await _db.MunicipioGrantSyncRequests.MaxAsync(r => r.Id);
        _db.ChangeTracker.Clear();

        var completed = completedAt?.UtcDateTime;
        var failed = failedAt?.UtcDateTime;
        var lease = leasedBy is null ? (DateTime?)null : Now.UtcDateTime.AddMinutes(2);
        await _db.MunicipioGrantSyncRequests.Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CompletedAt, completed)
            .SetProperty(r => r.FailedAt, failed)
            .SetProperty(r => r.LeasedBy, leasedBy)
            .SetProperty(r => r.LeaseExpiresAt, lease)
            .SetProperty(r => r.LastError, lastError));
        return id;
    }

    private async Task<Municipio> AddMunicipioAsync(string ibge)
    {
        var municipio = new Municipio { IbgeCode = ibge, Name = $"M{ibge}", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = PrefeituraA };
        var repo = new EFMunicipioRepository(_db);
        await repo.AddAsync(municipio);
        await repo.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return municipio;
    }

    private Task<List<long>> IdsAsync() =>
        _db.MunicipioGrantSyncRequests.AsNoTracking().OrderBy(r => r.Id).Select(r => r.Id).ToListAsync();

    private async Task<List<(string IbgeCode, MunicipioGrantSyncState State, string? Error, DateTimeOffset? SyncedAt)>> StatusesAsync()
    {
        _db.ChangeTracker.Clear();
        var all = await new EFMunicipioRepository(_db).GetAllAsync();
        _db.ChangeTracker.Clear();
        return all.OrderBy(m => m.IbgeCode, StringComparer.Ordinal).Select(m => (m.IbgeCode, m.GrantSyncState, m.GrantSyncError, m.GrantSyncedAt)).ToList();
    }
}
