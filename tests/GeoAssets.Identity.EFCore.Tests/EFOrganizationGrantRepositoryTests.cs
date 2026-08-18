using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

public class EFOrganizationGrantRepositoryTests
{
    private static GeoIdentityDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        return new GeoIdentityDbContext(options);
    }

    private static OrganizationGrant Grant(
        Guid granteeOrgId, Guid resourceOrgId, bool isActive = true, DateTime? expiresAt = null) => new()
    {
        GranteeOrganizationId  = granteeOrgId,
        ResourceOrganizationId = resourceOrgId,
        AllowedActions         = ["serviceorders:view", "features:read"],
        GrantedBy              = "admin@example.com",
        GrantedAt              = DateTime.UtcNow,
        IsActive               = isActive,
        ExpiresAt              = expiresAt,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllScalarFieldsIncludingAllowedActions()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFOrganizationGrantRepository(db);
        var grant = Grant(Guid.NewGuid(), Guid.NewGuid());
        grant.ResourceType = "ServiceOrder";
        grant.RequiredRole = "Supervisor";

        await repo.AddAsync(grant);
        await repo.SaveChangesAsync();
        var loaded = await repo.GetByIdAsync(grant.Id);

        loaded.Should().NotBeNull();
        loaded!.GranteeOrganizationId.Should().Be(grant.GranteeOrganizationId);
        loaded.ResourceOrganizationId.Should().Be(grant.ResourceOrganizationId);
        loaded.ResourceType.Should().Be("ServiceOrder");
        loaded.RequiredRole.Should().Be("Supervisor");
        loaded.AllowedActions.Should().BeEquivalentTo(["serviceorders:view", "features:read"]);
        loaded.GrantedBy.Should().Be("admin@example.com");
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_MatchingActiveUnexpiredGrant_IsReturned()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFOrganizationGrantRepository(db);
        var grantee = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, resource));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(grantee, resource);

        active.Should().ContainSingle();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_InactiveGrant_IsExcluded()
    {
        // Non-leakage: a grant explicitly deactivated must not authorize cross-org access,
        // even though its GranteeOrganizationId/ResourceOrganizationId still match.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFOrganizationGrantRepository(db);
        var grantee = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, resource, isActive: false));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(grantee, resource);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_ExpiredGrant_IsExcluded()
    {
        // Non-leakage: a grant past its ExpiresAt must not authorize cross-org access.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFOrganizationGrantRepository(db);
        var grantee = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, resource, expiresAt: DateTime.UtcNow.AddDays(-1)));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(grantee, resource);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_DifferentOrganizationPair_IsExcluded()
    {
        // Non-leakage: a grant scoped to one grantee/resource pair must not leak to another.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFOrganizationGrantRepository(db);
        await repo.AddAsync(Grant(Guid.NewGuid(), Guid.NewGuid()));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(Guid.NewGuid(), Guid.NewGuid());

        active.Should().BeEmpty();
    }
}
