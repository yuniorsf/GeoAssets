using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static GeoAssets.Server.Tests.OrganizationGrants.OrganizationGrantsHarness;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-394 — the upsert route when a concurrent write of the same key wins the race: one lost save is retried (re-read, then
/// <c>Unchanged</c>/<c>Updated</c>), a second one answers 409 <c>organization-grant.conflict</c> instead of a 500; an expired
/// grant that still holds the key is retired in favour of a new one.
/// </summary>
public sealed class OrganizationGrantsConflictTests
{
    // The conflict tests use a key with a resourceType: on SQLite (nulls distinct) the unique index only guards such keys.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Upsert_OneLostSaveToAConcurrentWriter_RetriesAndAnswersUnchangedWithTheWinnersGrant()
    {
        await using var harness = new OrganizationGrantsHarness();
        OrganizationGrant? winner = null;
        harness.DecorateRepository = inner => new ConflictingRepository(inner)
        {
            AfterFirstFind = async () => winner = await harness.SeedAsync(resourceType: "ServiceOrder"),
        };
        await harness.StartAsync();

        using var response = await harness.UpsertAsync(UpsertBody(resourceType: "ServiceOrder"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<OrganizationGrantUpsertResult>(Web))!;
        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Unchanged);
        result.Grant.Id.Should().Be(winner!.Id);
        (await harness.Db.OrganizationGrants.AsNoTracking().CountAsync(g => g.IsActive)).Should().Be(1);
        // The seeded winner wrote no outbox row (it bypasses the repository) and the loser's discarded write left none.
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_OneLostSaveToAConcurrentWriterWithOtherActions_RetriesAndUpdatesTheWinnersGrant()
    {
        await using var harness = new OrganizationGrantsHarness();
        OrganizationGrant? winner = null;
        harness.DecorateRepository = inner => new ConflictingRepository(inner)
        {
            AfterFirstFind = async () => winner = await harness.SeedAsync(resourceType: "ServiceOrder", actions: ["ip-billing:read"]),
        };
        await harness.StartAsync();

        using var response = await harness.UpsertAsync(UpsertBody(resourceType: "ServiceOrder", actions: ["ip-billing:write"]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<OrganizationGrantUpsertResult>(Web))!;
        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Updated);
        result.Grant.Id.Should().Be(winner!.Id);
        result.Grant.AllowedActions.Should().Equal("ip-billing:write");
        var stored = await harness.Db.OrganizationGrants.AsNoTracking().SingleAsync();
        stored.AllowedActions.Should().Equal("ip-billing:write");
    }

    [Fact]
    public async Task Upsert_TwoConflictsInARow_Answers409ConflictProblemNever500()
    {
        await using var harness = new OrganizationGrantsHarness();
        var repository = (ConflictingRepository?)null;
        harness.DecorateRepository = inner => repository = new ConflictingRepository(inner) { ConflictingSaves = 2, Db = harness.Db };
        await harness.StartAsync();

        using var response = await harness.UpsertAsync(UpsertBody());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be(OrganizationGrantsRestApiExtensions.ConflictProblemCode);
        repository!.Saves.Should().Be(2);
        (await harness.Db.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(0);
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_AnExpiredActiveGrantHoldsTheKey_RetiresItAndCreatesANewGrant()
    {
        await using var harness = new OrganizationGrantsHarness();
        await harness.StartAsync();
        var expired = await harness.SeedAsync(expiresAt: harness.Time.GetUtcNow().UtcDateTime.AddDays(-1));

        using var response = await harness.UpsertAsync(UpsertBody());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<OrganizationGrantUpsertResult>(Web))!;
        result.Outcome.Should().Be(OrganizationGrantUpsertOutcome.Created);
        result.Grant.Id.Should().NotBe(expired.Id);
        var all = await harness.Db.OrganizationGrants.AsNoTracking().ToListAsync();
        all.Should().HaveCount(2);
        all.Single(g => g.Id == expired.Id).IsActive.Should().BeFalse();
        all.Single(g => g.Id == result.Grant.Id).IsActive.Should().BeTrue();
        var outbox = await harness.OutboxAsync();
        outbox.Should().Contain(e => e.GrantId == expired.Id && !e.IsActive);
        outbox.Should().Contain(e => e.GrantId == result.Grant.Id && e.IsActive);
    }

    [Fact]
    public async Task Revoke_AnExpiredActiveGrant_IsStillIgnored()
    {
        await using var harness = new OrganizationGrantsHarness();
        await harness.StartAsync();
        var expired = await harness.SeedAsync(expiresAt: harness.Time.GetUtcNow().UtcDateTime.AddDays(-1));

        using var response = await harness.RevokeAsync(KeyBody());

        (await response.Content.ReadFromJsonAsync<OrganizationGrantRevokeResult>(Web))!.Revoked.Should().Be(0);
        (await harness.Db.OrganizationGrants.AsNoTracking().SingleAsync(g => g.Id == expired.Id)).IsActive.Should().BeTrue();
        (await harness.OutboxAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The real repository, except that it can run a hook after its first key lookup (a concurrent writer storing its grant
    /// between the read and the save: the real unique index then refuses the save) or fail its next saves with the typed
    /// conflict the way the real repository does (the tracked writes discarded first).
    /// </summary>
    private sealed class ConflictingRepository(IOrganizationGrantRepository inner) : IOrganizationGrantRepository
    {
        private bool _found;

        public Func<Task>? AfterFirstFind { get; init; }

        public int ConflictingSaves { get; init; }

        public DbContext? Db { get; init; }

        public int Saves { get; private set; }

        public async Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default)
        {
            var found = await inner.FindActiveGrantByKeyAsync(key, ct);
            if (!_found && AfterFirstFind is not null)
            {
                _found = true;
                await AfterFirstFind();
            }

            return found;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            if (Saves++ >= ConflictingSaves) return inner.SaveChangesAsync(ct);

            Db!.ChangeTracker.Clear();
            throw new OrganizationGrantKeyConflictException(key: null, new InvalidOperationException("forced"));
        }

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            inner.GetActiveGrantsAsync(granteeOrganizationId, resourceOrganizationId, ct);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            inner.GetActiveGrantsForGranteeAsync(granteeOrganizationId, ct);

        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => inner.AddAsync(grant, ct);

        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => inner.UpdateAsync(grant, ct);

        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) =>
            inner.GetSyncBatchAsync(afterSequence, take, ct);
    }
}
