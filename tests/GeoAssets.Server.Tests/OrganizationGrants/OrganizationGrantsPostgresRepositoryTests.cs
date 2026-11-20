using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests.OrganizationGrants;

/// <summary>
/// XD01-394 — <see cref="EFOrganizationGrantRepository"/> on PostgreSQL with the production migrations: the unique index
/// <c>UX_OrganizationGrants_ActiveKey</c> refuses a second active grant of a key (a null resource type or scope counting as one
/// value), the repository turns that refusal into <see cref="OrganizationGrantKeyConflictException"/> and leaves its context usable.
/// </summary>
[Collection("OrganizationGrantsPostgres")]
public sealed class OrganizationGrantsPostgresRepositoryTests
{
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
    public async Task SaveChangesAsync_SecondActiveGrantOfTheKey_ThrowsTypedConflictAndDiscardsTheWrite(string? resourceType, string? scopeId)
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var winnerDb = OrganizationGrantsPostgres.NewContext(connectionString);
        var winner = await StoreAsync(winnerDb, Grant(resourceType, scopeId));
        await using var loserDb = OrganizationGrantsPostgres.NewContext(connectionString);
        var loserRepository = Repository(loserDb);
        await loserRepository.AddAsync(Grant(resourceType, scopeId));

        var act = () => loserRepository.SaveChangesAsync();

        var thrown = (await act.Should().ThrowAsync<OrganizationGrantKeyConflictException>()).Which;
        thrown.Key.Should().Be(new OrganizationGrantKey(Grantee, Resource, resourceType, scopeId));
        loserDb.ChangeTracker.Entries().Should().BeEmpty();
        var outbox = await Repository(loserDb).GetSyncBatchAsync(0, 100);
        outbox.Should().ContainSingle().Which.GrantId.Should().Be(winner.Id);
        (await loserDb.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SaveChangesAsync_AfterAConflict_TheSameContextReadsTheStoredGrantAndSavesAnotherKey()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var winnerDb = OrganizationGrantsPostgres.NewContext(connectionString);
        var winner = await StoreAsync(winnerDb, Grant(null, null));
        await using var loserDb = OrganizationGrantsPostgres.NewContext(connectionString);
        var repository = Repository(loserDb);
        await repository.AddAsync(Grant(null, null));
        var conflict = () => repository.SaveChangesAsync();
        await conflict.Should().ThrowAsync<OrganizationGrantKeyConflictException>();

        var found = await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, null));
        var other = Grant(null, "other-scope");
        await repository.AddAsync(other);
        await repository.SaveChangesAsync();

        found!.Id.Should().Be(winner.Id);
        (await loserDb.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(2);
        (await repository.GetSyncBatchAsync(0, 100)).Select(e => e.GrantId).Should().BeEquivalentTo([winner.Id, other.Id]);
    }

    [Fact]
    public async Task SaveChangesAsync_ReactivatingAGrantOfAKeyWithAnActiveGrant_ThrowsTypedConflict()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        await StoreAsync(db, Grant("ServiceOrder", "1"));
        var inactive = Grant("ServiceOrder", "1");
        inactive.IsActive = false;
        await StoreAsync(db, inactive);
        var repository = Repository(db);

        inactive.IsActive = true;
        await repository.UpdateAsync(inactive);
        var act = () => repository.SaveChangesAsync();

        await act.Should().ThrowAsync<OrganizationGrantKeyConflictException>();
    }

    [Fact]
    public async Task SaveChangesAsync_InactiveOrDifferentKeyGrants_AreNotConflicts()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        await StoreAsync(db, Grant(null, null));

        var inactive = Grant(null, null);
        inactive.IsActive = false;
        await StoreAsync(db, inactive);
        await StoreAsync(db, Grant("ServiceOrder", null));
        await StoreAsync(db, Grant(null, "3550308"));

        (await db.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task SaveChangesAsync_UniqueViolationOfAnotherConstraint_IsNotTranslated()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var stored = await StoreAsync(db, Grant("ServiceOrder", "1"));
        await using var other = OrganizationGrantsPostgres.NewContext(connectionString);
        var repository = Repository(other);
        var samePrimaryKey = Grant("ServiceOrder", "2");
        samePrimaryKey.Id = stored.Id;
        await repository.AddAsync(samePrimaryKey);

        var act = () => repository.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>()).Which.Should().NotBeOfType<OrganizationGrantKeyConflictException>();
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_NullKeyParts_MatchOnlyGrantsWithNullParts()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var nulls = await StoreAsync(db, Grant(null, null));
        var typed = await StoreAsync(db, Grant("ServiceOrder", null));
        var scoped = await StoreAsync(db, Grant(null, "3550308"));
        var repository = Repository(db);

        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, null)))!.Id.Should().Be(nulls.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, "ServiceOrder", null)))!.Id.Should().Be(typed.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, "3550308")))!.Id.Should().Be(scoped.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, "ServiceOrder", "3550308"))).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_ExpiredActiveGrant_IsFoundAndInactiveGrantIsNot()
    {
        var connectionString = await OrganizationGrantsPostgres.CreateMigratedDatabaseAsync();
        await using var db = OrganizationGrantsPostgres.NewContext(connectionString);
        var expired = Grant("ServiceOrder", "1");
        expired.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(-1);
        await StoreAsync(db, expired);
        var inactive = Grant("ServiceOrder", "2");
        inactive.IsActive = false;
        await StoreAsync(db, inactive);
        var repository = Repository(db);

        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, "ServiceOrder", "1")))!.Id.Should().Be(expired.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, "ServiceOrder", "2"))).Should().BeNull();
    }

    private EFOrganizationGrantRepository Repository(GeoIdentityDbContext db) => new(db, _time);

    private async Task<OrganizationGrant> StoreAsync(GeoIdentityDbContext db, OrganizationGrant grant)
    {
        var repository = Repository(db);
        await repository.AddAsync(grant);
        await repository.SaveChangesAsync();
        return grant;
    }

    private OrganizationGrant Grant(string? resourceType, string? scopeId) => new()
    {
        GranteeOrganizationId = Grantee,
        ResourceOrganizationId = Resource,
        ResourceType = resourceType,
        ScopeId = scopeId,
        AllowedActions = ["ip-billing:read"],
        GrantedBy = "seed",
        GrantedAt = _time.GetUtcNow().UtcDateTime,
        IsActive = true,
    };
}
