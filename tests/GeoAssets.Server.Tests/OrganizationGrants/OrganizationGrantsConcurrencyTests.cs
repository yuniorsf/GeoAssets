using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-394 — N parallel upserts of one key on a real PostgreSQL database with the kernel's migrations (so the unique index
/// <c>UX_OrganizationGrants_ActiveKey</c> is the production one): one active grant, every call succeeds, exactly one
/// <see cref="OrganizationGrantUpsertOutcome.Created"/>, and the grant-sync outbox holds rows only for the stored grant.
/// </summary>
[Collection("OrganizationGrantsPostgres")]
public sealed class OrganizationGrantsConcurrencyTests
{
    private const int Callers = 16;

    private static readonly Guid Grantee = new("a0a0a0a0-0000-4000-8000-000000000394");
    private static readonly Guid Resource = new("b0b0b0b0-0000-4000-8000-000000000394");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    public static TheoryData<string?, string?> Keys => new()
    {
        { "ServiceOrder", "3550308" },
        { null, null },
        { "ServiceOrder", null },
        { null, "3550308" },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task UpsertAsync_ParallelUpsertsOfOneKey_StoreOneActiveGrantAndOneCreated(string? resourceType, string? scopeId)
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        var key = new OrganizationGrantKey(Grantee, Resource, resourceType, scopeId);
        var requests = Enumerable.Range(0, Callers).Select(_ => Request(key, ["ip-billing:read"])).ToList();

        var results = await UpsertInParallelAsync(connectionString, requests);

        results.Should().HaveCount(Callers);
        results.Count(r => r.Outcome == OrganizationGrantUpsertOutcome.Created).Should().Be(1);
        results.Where(r => r.Outcome != OrganizationGrantUpsertOutcome.Created)
            .Should().OnlyContain(r => r.Outcome == OrganizationGrantUpsertOutcome.Unchanged);
        results.Select(r => r.Grant.Id).Distinct().Should().ContainSingle();

        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var stored = await db.OrganizationGrants.AsNoTracking().ToListAsync();
        stored.Should().ContainSingle().Which.Should().Match<GeoAssets.Identity.Authorization.Models.OrganizationGrant>(
            g => g.IsActive && g.ResourceType == resourceType && g.ScopeId == scopeId && g.Id == results[0].Grant.Id);
        await AssertOutboxOnlyForStoredGrantsAsync(connectionString);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public async Task UpsertAsync_ParallelUpsertsWithDifferentActions_StoreOneActiveGrantEqualToOneRequest(string? resourceType, string? scopeId)
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        var key = new OrganizationGrantKey(Grantee, Resource, resourceType, scopeId);
        var requests = Enumerable.Range(0, Callers).Select(i => Request(key, [$"ip-billing:action-{i}"])).ToList();

        var results = await UpsertInParallelAsync(connectionString, requests);

        results.Count(r => r.Outcome == OrganizationGrantUpsertOutcome.Created).Should().Be(1);
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var stored = (await db.OrganizationGrants.AsNoTracking().ToListAsync()).Should().ContainSingle().Subject;
        stored.IsActive.Should().BeTrue();
        requests.Select(r => r.AllowedActions.Single()).Should().Contain(stored.AllowedActions.Single());
        await AssertOutboxOnlyForStoredGrantsAsync(connectionString);
    }

    [Fact]
    public async Task UpsertAsync_ParallelUpsertsOfDifferentKeys_EachKeyGetsItsOwnGrant()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        var requests = Enumerable.Range(0, Callers)
            .Select(i => Request(new OrganizationGrantKey(Grantee, Resource, null, $"scope-{i % 4}"), ["ip-billing:read"]))
            .ToList();

        var results = await UpsertInParallelAsync(connectionString, requests);

        results.Count(r => r.Outcome == OrganizationGrantUpsertOutcome.Created).Should().Be(4);
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        (await db.OrganizationGrants.AsNoTracking().Where(g => g.IsActive).CountAsync()).Should().Be(4);
        await AssertOutboxOnlyForStoredGrantsAsync(connectionString);
    }

    private static OrganizationGrantUpsertRequest Request(OrganizationGrantKey key, string[] actions) =>
        new(key, actions, null, null, "admin@test");

    private async Task<IReadOnlyList<OrganizationGrantUpsertResult>> UpsertInParallelAsync(
        string connectionString, List<OrganizationGrantUpsertRequest> requests)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;
        var tasks = requests.Select(request => Task.Run(async () =>
        {
            await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
            var service = new OrganizationGrantService(new EFOrganizationGrantRepository(db, _time), _time);
            if (Interlocked.Increment(ref ready) == requests.Count) gate.SetResult();
            await gate.Task;
            return await service.UpsertAsync(request, CancellationToken.None);
        })).ToList();

        return await Task.WhenAll(tasks);
    }

    private static async Task AssertOutboxOnlyForStoredGrantsAsync(string connectionString)
    {
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var storedIds = await db.OrganizationGrants.AsNoTracking().Select(g => g.Id).ToListAsync();
        var outbox = await new EFOrganizationGrantRepository(db, TimeProvider.System).GetSyncBatchAsync(0, 1000);
        outbox.Should().NotBeEmpty();
        outbox.Select(e => e.GrantId).Should().OnlyContain(id => storedIds.Contains(id));
        outbox.Where(e => e.IsActive).Select(e => e.GrantId).Distinct().Should().BeEquivalentTo(storedIds);
    }
}
