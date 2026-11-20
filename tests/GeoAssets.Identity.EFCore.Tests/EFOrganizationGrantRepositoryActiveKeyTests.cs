using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

/// <summary>
/// XD01-394 — the one-active-grant-per-key rule on SQLite (where the unique index guards only keys without a null part) and
/// <see cref="EFOrganizationGrantRepository.FindActiveGrantByKeyAsync"/>.
/// </summary>
public sealed class EFOrganizationGrantRepositoryActiveKeyTests
{
    private static readonly Guid Grantee = new("a0a0a0a0-0000-4000-8000-000000000394");
    private static readonly Guid Resource = new("b0b0b0b0-0000-4000-8000-000000000394");
    private static readonly OrganizationGrantKey Key = new(Grantee, Resource, "ServiceOrder", "3550308");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    private static GeoIdentityDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(connection).Options);

    private OrganizationGrant Grant(
        string? resourceType = "ServiceOrder", string? scopeId = "3550308", bool isActive = true, DateTime? expiresAt = null, DateTime? grantedAt = null) => new()
    {
        GranteeOrganizationId = Grantee,
        ResourceOrganizationId = Resource,
        ResourceType = resourceType,
        ScopeId = scopeId,
        AllowedActions = ["ip-billing:read"],
        GrantedBy = "seed",
        GrantedAt = grantedAt ?? _time.GetUtcNow().UtcDateTime,
        IsActive = isActive,
        ExpiresAt = expiresAt,
    };

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    private async Task StoreAsync(SqliteConnection connection, params OrganizationGrant[] grants)
    {
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);
        foreach (var grant in grants) await repository.AddAsync(grant);
        await repository.SaveChangesAsync();
    }

    private async Task<Exception?> TryStoreAsync(SqliteConnection connection, OrganizationGrant grant)
    {
        try
        {
            await StoreAsync(connection, grant);
            return null;
        }
        catch (DbUpdateException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task SaveChangesAsync_SecondActiveGrantOfTheKey_ThrowsTypedConflictWithTheKeyAndDiscardsTheWrite()
    {
        await using var connection = await OpenAsync();
        var winner = Grant();
        await StoreAsync(connection, winner);
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);
        await repository.AddAsync(Grant());

        var act = () => repository.SaveChangesAsync();

        (await act.Should().ThrowAsync<OrganizationGrantKeyConflictException>()).Which.Key.Should().Be(Key);
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await repository.GetSyncBatchAsync(0, 10)).Should().ContainSingle().Which.GrantId.Should().Be(winner.Id);
        (await repository.FindActiveGrantByKeyAsync(Key))!.Id.Should().Be(winner.Id);
        await repository.AddAsync(Grant(scopeId: "other"));
        await repository.SaveChangesAsync();
        (await db.OrganizationGrants.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SaveChangesAsync_InactiveGrantOfTheKey_IsNotAConflict()
    {
        await using var connection = await OpenAsync();
        await StoreAsync(connection, Grant());

        var thrown = await TryStoreAsync(connection, Grant(isActive: false));

        thrown.Should().BeNull();
    }

    [Fact]
    public async Task SaveChangesAsync_UniqueViolationOfAnotherConstraint_IsNotTranslated()
    {
        await using var connection = await OpenAsync();
        var stored = Grant();
        await StoreAsync(connection, stored);
        var samePrimaryKey = Grant(scopeId: "other");
        samePrimaryKey.Id = stored.Id;

        var thrown = await TryStoreAsync(connection, samePrimaryKey);

        thrown.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChangesAsync_NonUniqueFailure_IsNotTranslated()
    {
        await using var connection = await OpenAsync();
        var invalid = Grant();
        invalid.GrantedBy = null!;

        var thrown = await TryStoreAsync(connection, invalid);

        thrown.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_NullKeyParts_MatchOnlyGrantsWithNullParts()
    {
        await using var connection = await OpenAsync();
        var nulls = Grant(null, null);
        var typed = Grant("ServiceOrder", null);
        var scoped = Grant(null, "3550308");
        await StoreAsync(connection, nulls, typed, scoped);
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);

        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, null)))!.Id.Should().Be(nulls.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, "ServiceOrder", null)))!.Id.Should().Be(typed.Id);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, "3550308")))!.Id.Should().Be(scoped.Id);
        (await repository.FindActiveGrantByKeyAsync(Key)).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_ExpiredActiveGrant_IsFoundAndInactiveGrantIsNot()
    {
        await using var connection = await OpenAsync();
        var expired = Grant(expiresAt: _time.GetUtcNow().UtcDateTime.AddDays(-1));
        await StoreAsync(connection, expired, Grant(scopeId: "2", isActive: false));
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);

        (await repository.FindActiveGrantByKeyAsync(Key))!.Id.Should().Be(expired.Id);
        (await repository.FindActiveGrantByKeyAsync(Key with { ScopeId = "2" })).Should().BeNull();
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_SeveralActiveGrantsOfAKeyWithNullParts_ReturnsTheOldestThenLowestId()
    {
        await using var connection = await OpenAsync();
        var start = _time.GetUtcNow().UtcDateTime;
        var newest = Grant(null, null, grantedAt: start.AddDays(2));
        var oldest = Grant(null, null, grantedAt: start);
        await StoreAsync(connection, newest, oldest);
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);

        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, null)))!.Id.Should().Be(oldest.Id);

        var tieLow = Grant(null, "tie", grantedAt: start);
        var tieHigh = Grant(null, "tie", grantedAt: start);
        if (tieLow.Id.CompareTo(tieHigh.Id) > 0) (tieLow, tieHigh) = (tieHigh, tieLow);
        await StoreAsync(connection, tieHigh, tieLow);
        (await repository.FindActiveGrantByKeyAsync(new OrganizationGrantKey(Grantee, Resource, null, "tie")))!.Id.Should().Be(tieLow.Id);
    }

    [Fact]
    public async Task FindActiveGrantByKeyAsync_NullKey_Throws()
    {
        await using var connection = await OpenAsync();
        await using var db = NewContext(connection);
        var repository = new EFOrganizationGrantRepository(db, _time);

        var act = () => repository.FindActiveGrantByKeyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
