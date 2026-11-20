using FluentAssertions;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static GeoAssets.IpBilling.EFCore.Tests.GrantSyncSqliteFixture;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>XD01-395: the município grant-sync outbox (<see cref="EFMunicipioGrantSyncOutbox"/>) on SQLite.</summary>
public sealed class EFMunicipioGrantSyncOutboxTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    // ── enqueue ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_CommitsWithTheMunicipioSave_AndMapsEveryField()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = new Municipio { IbgeCode = "3550308", Name = "São Paulo", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = PrefeituraB };
        var oldDistribuidora = Guid.NewGuid();
        var change = new MunicipioGrantChange(
            municipio.Id, MunicipioGrantSyncKind.Sync, "3550308", DistribuidoraOrg, PrefeituraB, PrefeituraA, "3304557", oldDistribuidora);

        await using (var db = fixture.NewContext())
        {
            var repo = new EFMunicipioRepository(db);
            await repo.AddAsync(municipio);
            fixture.NewOutbox(db).Enqueue(change, "user-1");
            await repo.SaveChangesAsync();
        }

        await using var read = fixture.NewContext();
        (await read.Municipios.CountAsync()).Should().Be(1);
        var row = await read.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync();
        row.MunicipioId.Should().Be(municipio.Id);
        row.Kind.Should().Be(MunicipioGrantSyncKind.Sync);
        row.IbgeCode.Should().Be("3550308");
        row.DistribuidoraOrganizationId.Should().Be(DistribuidoraOrg);
        row.PrefeituraOrganizationId.Should().Be(PrefeituraB);
        row.PreviousPrefeituraOrganizationId.Should().Be(PrefeituraA);
        row.PreviousIbgeCode.Should().Be("3304557");
        row.PreviousDistribuidoraOrganizationId.Should().Be(oldDistribuidora);
        row.RequestedBy.Should().Be("user-1");
        row.RequestedAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        row.NextAttemptAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        row.Attempts.Should().Be(0);
        row.LeasedBy.Should().BeNull();
        row.CompletedAt.Should().BeNull();
        row.FailedAt.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_RollsBackWithAFailedMunicipioSave()
    {
        using var fixture = new GrantSyncSqliteFixture();
        await fixture.AddMunicipioAsync("3550308");

        await using (var db = fixture.NewContext())
        {
            var repo = new EFMunicipioRepository(db);
            var duplicate = new Municipio { IbgeCode = "3550308", Name = "Duplicate", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg };
            await repo.AddAsync(duplicate);
            fixture.NewOutbox(db).Enqueue(Sync(duplicate.Id), "user-1");

            var act = () => repo.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>("the IBGE code is unique");
        }

        await using var read = fixture.NewContext();
        (await read.MunicipioGrantSyncRequests.CountAsync()).Should().Be(0, "the request is committed with the município or not at all");
    }

    [Fact]
    public async Task Enqueue_WithoutASave_PersistsNothing()
    {
        using var fixture = new GrantSyncSqliteFixture();

        await using (var db = fixture.NewContext())
        {
            fixture.NewOutbox(db).Enqueue(Sync(Guid.NewGuid()), "user-1");
        }

        await using var read = fixture.NewContext();
        (await read.MunicipioGrantSyncRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Enqueue_BoundsAVeryLongRequester()
    {
        using var fixture = new GrantSyncSqliteFixture();

        await fixture.QueueAsync(Sync(Guid.NewGuid()), new string('u', 1000));

        await using var read = fixture.NewContext();
        (await read.MunicipioGrantSyncRequests.SingleAsync()).RequestedBy.Should().HaveLength(256);
    }

    [Fact]
    public void Enqueue_RejectsABlankRequester_AndABlankIbgeCode()
    {
        using var fixture = new GrantSyncSqliteFixture();
        using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);

        var blankRequester = () => outbox.Enqueue(Sync(Guid.NewGuid()), " ");
        var blankIbge = () => outbox.Enqueue(Sync(Guid.NewGuid(), ibge: " "), "user-1");
        var nullChange = () => outbox.Enqueue(null!, "user-1");

        blankRequester.Should().Throw<ArgumentException>();
        blankIbge.Should().Throw<ArgumentException>();
        nullChange.Should().Throw<ArgumentNullException>();
    }

    // ── claim ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimDue_ReturnsTheWorkItem_WithItsFieldsAndAttempts()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipioId = Guid.NewGuid();
        var change = new MunicipioGrantChange(municipioId, MunicipioGrantSyncKind.Revoke, "3550308", DistribuidoraOrg, PrefeituraA);
        var id = await fixture.QueueAsync(change, "user-9");

        await using var db = fixture.NewContext();
        var claimed = await fixture.NewOutbox(db).ClaimDueAsync(5, Lease, "worker-1");

        var item = claimed.Should().ContainSingle().Subject;
        item.Id.Should().Be(id);
        item.Change.Should().Be(change);
        item.RequestedBy.Should().Be("user-9");
        item.RequestedAt.Should().Be(fixture.Time.GetUtcNow());
        item.Attempts.Should().Be(0);
        var row = await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync();
        row.LeasedBy.Should().Be("worker-1");
        row.LeaseExpiresAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime + Lease);
    }

    [Fact]
    public async Task ClaimDue_ReturnsOnlyTheOldestUnfinishedRequestOfEachMunicipio_OldestFirst()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var x1 = await fixture.QueueAsync(Sync(x));
        var y1 = await fixture.QueueAsync(Sync(y));
        await fixture.QueueAsync(Sync(x, prefeitura: PrefeituraB));

        await using var db = fixture.NewContext();
        var claimed = await fixture.NewOutbox(db).ClaimDueAsync(10, Lease, "worker-1");

        claimed.Select(c => c.Id).Should().Equal(x1, y1);
        claimed.Select(c => c.Change.MunicipioId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ClaimDue_RespectsTheBatchSize()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++) ids.Add(await fixture.QueueAsync(Sync(Guid.NewGuid())));

        await using var db = fixture.NewContext();
        var claimed = await fixture.NewOutbox(db).ClaimDueAsync(3, Lease, "worker-1");

        claimed.Select(c => c.Id).Should().Equal(ids.Take(3));
    }

    [Fact]
    public async Task ClaimDue_ALaterRequestOfAMunicipio_WaitsForTheEarlierOneToFinish()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = Guid.NewGuid();
        var first = await fixture.QueueAsync(Sync(municipio));
        var second = await fixture.QueueAsync(Sync(municipio, prefeitura: PrefeituraB));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);

        (await outbox.ClaimDueAsync(10, Lease, "worker-1")).Select(c => c.Id).Should().Equal(first);
        (await outbox.ClaimDueAsync(10, Lease, "worker-2")).Should().BeEmpty("the first is leased, the second is behind it");
        (await outbox.MarkCompletedAsync(first, "worker-1")).Should().BeTrue();

        (await outbox.ClaimDueAsync(10, Lease, "worker-2")).Select(c => c.Id).Should().Equal(second);
    }

    [Fact]
    public async Task ClaimDue_SkipsALeasedRequest_UntilTheLeaseExpires_ThenAnotherWorkerTakesIt()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        (await outbox.ClaimDueAsync(10, Lease, "crashed")).Should().ContainSingle();

        (await outbox.ClaimDueAsync(10, Lease, "worker-2")).Should().BeEmpty();
        fixture.Time.Advance(Lease - TimeSpan.FromSeconds(1));
        (await outbox.ClaimDueAsync(10, Lease, "worker-2")).Should().BeEmpty("still one second left");
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var taken = await outbox.ClaimDueAsync(10, Lease, "worker-2");

        taken.Select(c => c.Id).Should().Equal(id);
        (await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync()).LeasedBy.Should().Be("worker-2");
    }

    [Fact]
    public async Task ClaimDue_SkipsARequestWhoseRetryIsNotDueYet()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.ScheduleRetryAsync(id, "worker-1", "kernel down", fixture.Time.GetUtcNow().AddSeconds(30));

        (await outbox.ClaimDueAsync(10, Lease, "worker-1")).Should().BeEmpty();
        fixture.Time.Advance(TimeSpan.FromSeconds(30));

        (await outbox.ClaimDueAsync(10, Lease, "worker-1")).Should().ContainSingle().Which.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task ClaimDue_NeverReturnsACompletedOrFailedRequest()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var done = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        var failed = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkCompletedAsync(done, "worker-1");
        await outbox.MarkFailedAsync(failed, "worker-1", "gave up");
        fixture.Time.Advance(TimeSpan.FromHours(1));

        (await outbox.ClaimDueAsync(10, Lease, "worker-2")).Should().BeEmpty();
    }

    [Fact]
    public async Task ClaimDue_TwoConcurrentClaimers_NeverClaimTheSameRequest()
    {
        using var fixture = new GrantSyncSqliteFixture();
        const int municipios = 12;
        for (var i = 0; i < municipios; i++) await fixture.QueueAsync(Sync(Guid.NewGuid()));

        await using var dbA = fixture.NewContext();
        await using var dbB = fixture.NewContext();
        var outboxA = fixture.NewOutbox(dbA);
        var outboxB = fixture.NewOutbox(dbB);

        var results = await Task.WhenAll(
            Task.Run(() => outboxA.ClaimDueAsync(municipios, Lease, "worker-a")),
            Task.Run(() => outboxB.ClaimDueAsync(municipios, Lease, "worker-b")));

        var claimedIds = results.SelectMany(r => r).Select(c => c.Id).ToList();
        claimedIds.Should().OnlyHaveUniqueItems("a lease can be won by one worker only");
        claimedIds.Should().HaveCount(municipios);
    }

    [Theory]
    [InlineData(0, 1, "w")]
    [InlineData(1, 0, "w")]
    [InlineData(1, 1, " ")]
    public async Task ClaimDue_RejectsInvalidArguments(int batchSize, int leaseSeconds, string worker)
    {
        using var fixture = new GrantSyncSqliteFixture();
        await using var db = fixture.NewContext();

        var act = () => fixture.NewOutbox(db).ClaimDueAsync(batchSize, TimeSpan.FromSeconds(leaseSeconds), worker);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── outcomes ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkCompleted_ByTheLeaseHolder_Completes_AndReleasesTheLease_OnlyOnce()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");

        (await outbox.MarkCompletedAsync(id, "worker-1")).Should().BeTrue();
        (await outbox.MarkCompletedAsync(id, "worker-1")).Should().BeFalse("a completed request is no longer leased");

        var row = await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync();
        row.CompletedAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        row.LeasedBy.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Outcomes_FromAWorkerThatDoesNotHoldTheLease_AreIgnored()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");

        (await outbox.MarkCompletedAsync(id, "intruder")).Should().BeFalse();
        (await outbox.ScheduleRetryAsync(id, "intruder", "x", fixture.Time.GetUtcNow())).Should().BeFalse();
        (await outbox.MarkFailedAsync(id, "intruder", "x")).Should().BeFalse();

        var row = await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync();
        row.CompletedAt.Should().BeNull();
        row.FailedAt.Should().BeNull();
        row.Attempts.Should().Be(0);
        row.LeasedBy.Should().Be("worker-1");
    }

    [Fact]
    public async Task Outcomes_FromAWorkerWhoseLeaseWasTakenOver_AreIgnored_AndTheNewHoldersAreKept()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "slow");
        fixture.Time.Advance(Lease);
        await outbox.ClaimDueAsync(10, Lease, "fast");

        (await outbox.MarkCompletedAsync(id, "slow")).Should().BeFalse();
        (await outbox.MarkCompletedAsync(id, "fast")).Should().BeTrue();
    }

    [Fact]
    public async Task ScheduleRetry_CountsTheAttempt_KeepsTheBoundedError_ReleasesTheLease_AndSetsTheNextAttempt()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var id = await fixture.QueueAsync(Sync(Guid.NewGuid()));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        var next = fixture.Time.GetUtcNow().AddSeconds(7);

        (await outbox.ScheduleRetryAsync(id, "worker-1", new string('e', 5000), next)).Should().BeTrue();

        var row = await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync();
        row.Attempts.Should().Be(1);
        row.LastError.Should().HaveLength(1024);
        row.NextAttemptAt.Should().Be(next.UtcDateTime);
        row.LeasedBy.Should().BeNull();
        row.LeaseExpiresAt.Should().BeNull();
        row.CompletedAt.Should().BeNull();
        row.FailedAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailed_GivesUp_KeepsTheRow_AndFreesTheNextRequestOfTheMunicipio()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = Guid.NewGuid();
        var first = await fixture.QueueAsync(Sync(municipio));
        var second = await fixture.QueueAsync(Sync(municipio, prefeitura: PrefeituraB));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");

        (await outbox.MarkFailedAsync(first, "worker-1", "gave up")).Should().BeTrue();

        var row = await db.MunicipioGrantSyncRequests.AsNoTracking().SingleAsync(r => r.Id == first);
        row.FailedAt.Should().Be(fixture.Time.GetUtcNow().UtcDateTime);
        row.Attempts.Should().Be(1);
        row.LastError.Should().Be("gave up");
        row.LeasedBy.Should().BeNull();
        (await outbox.ClaimDueAsync(10, Lease, "worker-1")).Select(c => c.Id).Should().Equal(second);
    }

    [Fact]
    public async Task Outcomes_RejectBlankArguments()
    {
        using var fixture = new GrantSyncSqliteFixture();
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);

        var blankWorker = () => outbox.MarkCompletedAsync(1, " ");
        var blankRetry = () => outbox.ScheduleRetryAsync(1, "w", " ", fixture.Time.GetUtcNow());
        var blankFailure = () => outbox.MarkFailedAsync(1, "w", " ");

        await blankWorker.Should().ThrowAsync<ArgumentException>();
        await blankRetry.Should().ThrowAsync<ArgumentException>();
        await blankFailure.Should().ThrowAsync<ArgumentException>();
    }

    // ── the status of a município ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_OfAMunicipioWithoutRequests_IsNone()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = await fixture.AddMunicipioAsync("3550308");

        await using var db = fixture.NewContext();
        var loaded = await new EFMunicipioRepository(db).GetByIdAsync(municipio.Id);

        loaded!.GrantSyncState.Should().Be(MunicipioGrantSyncState.None);
        loaded.GrantSyncError.Should().BeNull();
        loaded.GrantSyncedAt.Should().BeNull();
    }

    [Fact]
    public async Task Status_FollowsTheLatestRequest_ThroughItsLifecycle()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = await fixture.AddMunicipioAsync("3550308", PrefeituraA);
        var id = await fixture.QueueAsync(Sync(municipio.Id));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        var repo = new EFMunicipioRepository(db);

        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Pending);

        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.ScheduleRetryAsync(id, "worker-1", "HttpRequestException: down", fixture.Time.GetUtcNow().AddSeconds(5));
        var retrying = (await repo.GetByIdAsync(municipio.Id))!;
        retrying.GrantSyncState.Should().Be(MunicipioGrantSyncState.Pending);
        retrying.GrantSyncError.Should().Be("HttpRequestException: down");
        retrying.GrantSyncedAt.Should().BeNull();

        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkCompletedAsync(id, "worker-1");
        var synced = (await repo.GetByIbgeCodeAsync("3550308"))!;
        synced.GrantSyncState.Should().Be(MunicipioGrantSyncState.Synced);
        synced.GrantSyncedAt.Should().Be(fixture.Time.GetUtcNow());
        synced.GrantSyncError.Should().BeNull("a synced município shows no error");
    }

    [Fact]
    public async Task Status_OfARequestThatRanOutOfAttempts_IsFailed_WithItsError()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = await fixture.AddMunicipioAsync("3550308", PrefeituraA);
        var id = await fixture.QueueAsync(Sync(municipio.Id));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkFailedAsync(id, "worker-1", "InvalidOperationException: refused");

        var loaded = await new EFMunicipioRepository(db).GetByIdAsync(municipio.Id);

        loaded!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Failed);
        loaded.GrantSyncError.Should().Be("InvalidOperationException: refused");
    }

    [Fact]
    public async Task Status_TheLatestRequestWins_OverEarlierOnes()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = await fixture.AddMunicipioAsync("3550308", PrefeituraA);
        var first = await fixture.QueueAsync(Sync(municipio.Id));
        await using var db = fixture.NewContext();
        var outbox = fixture.NewOutbox(db);
        var repo = new EFMunicipioRepository(db);
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkCompletedAsync(first, "worker-1");

        var second = await fixture.QueueAsync(Sync(municipio.Id, prefeitura: PrefeituraB));
        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Pending, "a synced first request does not hide the queued second");

        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkFailedAsync(second, "worker-1", "gave up");
        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Failed);

        var third = await fixture.QueueAsync(Sync(municipio.Id, prefeitura: PrefeituraA));
        await outbox.ClaimDueAsync(10, Lease, "worker-1");
        await outbox.MarkCompletedAsync(third, "worker-1");
        (await repo.GetByIdAsync(municipio.Id))!.GrantSyncState.Should().Be(MunicipioGrantSyncState.Synced, "a later success supersedes an earlier failure");
    }

    [Fact]
    public async Task Status_IsReadForEveryMunicipioOfAList_AcrossMoreThanOneBatch()
    {
        using var fixture = new GrantSyncSqliteFixture();
        const int count = EFMunicipioRepository.GrantSyncStatusBatchSize * 2 + 37;
        await using (var seed = fixture.NewContext())
        {
            var repo = new EFMunicipioRepository(seed);
            var outbox = fixture.NewOutbox(seed);
            for (var i = 0; i < count; i++)
            {
                var municipio = new Municipio { IbgeCode = $"{1000000 + i}", Name = $"M{i}", Uf = "SP", DistribuidoraOrganizationId = DistribuidoraOrg, PrefeituraOrganizationId = PrefeituraA };
                await repo.AddAsync(municipio);
                if (i % 3 != 0) outbox.Enqueue(Sync(municipio.Id, municipio.IbgeCode), "user-1");
            }

            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("UPDATE \"MunicipioGrantSyncRequests\" SET \"CompletedAt\" = \"RequestedAt\" WHERE \"Id\" % 2 = 0");
        }

        await using var db = fixture.NewContext();
        var all = await new EFMunicipioRepository(db).GetAllAsync();

        all.Should().HaveCount(count);
        var byIndex = all.ToDictionary(m => int.Parse(m.IbgeCode, System.Globalization.CultureInfo.InvariantCulture) - 1000000);
        byIndex.Where(kv => kv.Key % 3 == 0).Should().OnlyContain(kv => kv.Value.GrantSyncState == MunicipioGrantSyncState.None);
        byIndex.Where(kv => kv.Key % 3 != 0).Should().OnlyContain(kv => kv.Value.GrantSyncState != MunicipioGrantSyncState.None);
        all.Count(m => m.GrantSyncState == MunicipioGrantSyncState.Synced).Should().BeGreaterThan(0);
        all.Count(m => m.GrantSyncState == MunicipioGrantSyncState.Pending).Should().BeGreaterThan(0);
        byIndex.Where(kv => kv.Key >= EFMunicipioRepository.GrantSyncStatusBatchSize * 2 && kv.Key % 3 != 0)
            .Should().OnlyContain(kv => kv.Value.GrantSyncState != MunicipioGrantSyncState.None, "the municípios of the last batch carry their status too");
    }

    [Fact]
    public async Task Status_ADeletedMunicipiosRequestsOutliveIt()
    {
        using var fixture = new GrantSyncSqliteFixture();
        var municipio = await fixture.AddMunicipioAsync("3550308", PrefeituraA);

        await using (var db = fixture.NewContext())
        {
            var repo = new EFMunicipioRepository(db);
            await repo.DeleteAsync(municipio.Id);
            fixture.NewOutbox(db).Enqueue(new MunicipioGrantChange(municipio.Id, MunicipioGrantSyncKind.Revoke, "3550308", DistribuidoraOrg, PrefeituraA), "user-1");
            await repo.SaveChangesAsync();
        }

        await using var read = fixture.NewContext();
        (await read.Municipios.CountAsync()).Should().Be(0);
        (await read.MunicipioGrantSyncRequests.CountAsync()).Should().Be(1, "there is no foreign key to the município");
    }
}
