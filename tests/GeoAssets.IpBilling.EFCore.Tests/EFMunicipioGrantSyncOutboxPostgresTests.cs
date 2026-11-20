using FluentAssertions;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// XD01-395: the outbox's claim, lease and status semantics on the real provider (PostgreSQL, the migrated schema):
/// UTC timestamps through <c>timestamptz</c>, the conditional <c>UPDATE</c> that takes a lease, and two claimers that
/// really run side by side.
/// </summary>
public sealed class EFMunicipioGrantSyncOutboxPostgresTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly Guid DistribuidoraOrg = new("d0d0d0d0-0000-4000-8000-000000000395");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000395");

    private static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    private static MunicipioGrantChange Sync(Guid municipioId, string ibge = "3550308") =>
        new(municipioId, MunicipioGrantSyncKind.Sync, ibge, DistribuidoraOrg, PrefeituraA);

    private static async Task<long> QueueAsync(IpBillingDbContext db, FakeTimeProvider time, MunicipioGrantChange change)
    {
        new EFMunicipioGrantSyncOutbox(db, time).Enqueue(change, "requester-1");
        await db.SaveChangesAsync();
        return await db.MunicipioGrantSyncRequests.MaxAsync(r => r.Id);
    }

    [Fact]
    public async Task Lifecycle_ClaimRetryClaimComplete_ShowsPendingThenSynced_WithUtcTimes()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        var time = NewClock();
        var outbox = new EFMunicipioGrantSyncOutbox(db, time);
        var municipio = new Municipio { IbgeCode = "3550308", Name = "São Paulo", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = PrefeituraA };
        var repo = new EFMunicipioRepository(db);
        await repo.AddAsync(municipio);
        outbox.Enqueue(Sync(municipio.Id), "requester-1");
        await repo.SaveChangesAsync();
        var id = await db.MunicipioGrantSyncRequests.Select(r => r.Id).SingleAsync();

        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Pending);
        var claimed = await outbox.ClaimDueAsync(5, Lease, "worker-1");
        claimed.Should().ContainSingle().Which.RequestedAt.Should().Be(time.GetUtcNow());
        (await outbox.ClaimDueAsync(5, Lease, "worker-2")).Should().BeEmpty();

        (await outbox.ScheduleRetryAsync(id, "worker-1", "HttpRequestException: down", time.GetUtcNow().AddSeconds(10))).Should().BeTrue();
        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncError.Should().Be("HttpRequestException: down");
        (await outbox.ClaimDueAsync(5, Lease, "worker-1")).Should().BeEmpty("the retry is not due");
        time.Advance(TimeSpan.FromSeconds(10));
        (await outbox.ClaimDueAsync(5, Lease, "worker-1")).Should().ContainSingle().Which.Attempts.Should().Be(1);
        (await outbox.MarkCompletedAsync(id, "worker-1")).Should().BeTrue();

        var synced = (await repo.GetByIdAsync(municipio.Id))!;
        synced.GrantSyncState.Should().Be(MunicipioGrantSyncState.Synced);
        synced.GrantSyncedAt.Should().Be(time.GetUtcNow());
    }

    [Fact]
    public async Task ClaimDue_TwoConcurrentClaimers_NeverClaimTheSameRequest()
    {
        var database = IpBillingPostgres.NewDatabaseName();
        await using var seed = await IpBillingPostgres.NewContextAsync(database);
        await seed.Database.MigrateAsync();
        var time = NewClock();
        const int municipios = 30;
        for (var i = 0; i < municipios; i++) await QueueAsync(seed, time, Sync(Guid.NewGuid()));

        await using var dbA = await IpBillingPostgres.NewContextAsync(database);
        await using var dbB = await IpBillingPostgres.NewContextAsync(database);
        var outboxA = new EFMunicipioGrantSyncOutbox(dbA, time);
        var outboxB = new EFMunicipioGrantSyncOutbox(dbB, time);

        var results = await Task.WhenAll(
            Task.Run(() => outboxA.ClaimDueAsync(municipios, Lease, "worker-a")),
            Task.Run(() => outboxB.ClaimDueAsync(municipios, Lease, "worker-b")));

        var ids = results.SelectMany(r => r).Select(c => c.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().HaveCount(municipios);
    }

    [Fact]
    public async Task ExpiredLease_IsTakenOver_AndTheStaleHoldersOutcomeIsIgnored()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        var time = NewClock();
        var outbox = new EFMunicipioGrantSyncOutbox(db, time);
        var id = await QueueAsync(db, time, Sync(Guid.NewGuid()));
        await outbox.ClaimDueAsync(5, Lease, "slow");
        time.Advance(Lease);

        (await outbox.ClaimDueAsync(5, Lease, "fast")).Should().ContainSingle().Which.Id.Should().Be(id);

        (await outbox.MarkCompletedAsync(id, "slow")).Should().BeFalse();
        (await outbox.MarkFailedAsync(id, "slow", "late")).Should().BeFalse();
        (await outbox.MarkCompletedAsync(id, "fast")).Should().BeTrue();
    }

    [Fact]
    public async Task MarkFailed_FreesTheNextRequestOfTheMunicipio_AndLaterOnesStayBehindUnfinishedOnes()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        var time = NewClock();
        var outbox = new EFMunicipioGrantSyncOutbox(db, time);
        var municipio = Guid.NewGuid();
        var first = await QueueAsync(db, time, Sync(municipio));
        var second = await QueueAsync(db, time, Sync(municipio, "3304557"));
        await QueueAsync(db, time, Sync(municipio, "3106200"));

        (await outbox.ClaimDueAsync(10, Lease, "w")).Select(c => c.Id).Should().Equal(first);
        await outbox.MarkFailedAsync(first, "w", "gave up");
        (await outbox.ClaimDueAsync(10, Lease, "w")).Select(c => c.Id).Should().Equal(second);
    }

    [Fact]
    public async Task Enqueue_RollsBackWithAFailedMunicipioSave()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        var time = NewClock();
        var repo = new EFMunicipioRepository(db);
        await repo.AddAsync(new Municipio { IbgeCode = "3550308", Name = "São Paulo", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg });
        await repo.SaveChangesAsync();

        var duplicate = new Municipio { IbgeCode = "3550308", Name = "Duplicate", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg };
        await repo.AddAsync(duplicate);
        new EFMunicipioGrantSyncOutbox(db, time).Enqueue(Sync(duplicate.Id), "user-1");

        var act = () => repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        (await db.MunicipioGrantSyncRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Status_IsReadForMoreThanOneBatchOfMunicipios()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        var time = NewClock();
        var repo = new EFMunicipioRepository(db);
        var outbox = new EFMunicipioGrantSyncOutbox(db, time);
        const int count = EFMunicipioRepository.GrantSyncStatusBatchSize + 101;
        for (var i = 0; i < count; i++)
        {
            var municipio = new Municipio { IbgeCode = $"{2000000 + i}", Name = $"M{i}", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = PrefeituraA };
            await repo.AddAsync(municipio);
            outbox.Enqueue(Sync(municipio.Id, municipio.IbgeCode), "user-1");
        }

        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE \"MunicipioGrantSyncRequests\" SET \"CompletedAt\" = \"RequestedAt\" WHERE \"Id\" % 2 = 0");
        db.ChangeTracker.Clear();

        var all = await repo.GetAllAsync();

        all.Should().HaveCount(count).And.OnlyContain(m => m.GrantSyncState != MunicipioGrantSyncState.None);
        all.Count(m => m.GrantSyncState == MunicipioGrantSyncState.Synced).Should().BeGreaterThan(100);
        all.Count(m => m.GrantSyncState == MunicipioGrantSyncState.Pending).Should().BeGreaterThan(100);
    }
}
