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
        GrantedAt              = TimeProvider.System.GetUtcNow().UtcDateTime,
        IsActive               = isActive,
        ExpiresAt              = expiresAt,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllScalarFieldsIncludingAllowedActions()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        var grant = Grant(Guid.NewGuid(), Guid.NewGuid());
        grant.ResourceType = "ServiceOrder";
        grant.RequiredRole = "Supervisor";
        grant.ScopeId = "municipio-1234567";

        await repo.AddAsync(grant);
        await repo.SaveChangesAsync();
        var loaded = await repo.GetByIdAsync(grant.Id);

        loaded.Should().NotBeNull();
        loaded!.GranteeOrganizationId.Should().Be(grant.GranteeOrganizationId);
        loaded.ResourceOrganizationId.Should().Be(grant.ResourceOrganizationId);
        loaded.ResourceType.Should().Be("ServiceOrder");
        loaded.RequiredRole.Should().Be("Supervisor");
        loaded.ScopeId.Should().Be("municipio-1234567");
        loaded.AllowedActions.Should().BeEquivalentTo(["serviceorders:view", "features:read"]);
        loaded.GrantedBy.Should().Be("admin@example.com");
        loaded.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_MatchingActiveUnexpiredGrant_IsReturned()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
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
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
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
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        var grantee = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, resource, expiresAt: TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(-1)));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(grantee, resource);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveGrantsAsync_DifferentOrganizationPair_IsExcluded()
    {
        // Non-leakage: a grant scoped to one grantee/resource pair must not leak to another.
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        await repo.AddAsync(Grant(Guid.NewGuid(), Guid.NewGuid()));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsAsync(Guid.NewGuid(), Guid.NewGuid());

        active.Should().BeEmpty();
    }

    // ── GetActiveGrantsForGranteeAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetActiveGrantsForGranteeAsync_ReturnsGrantsAcrossMultipleResourceOrganizations()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        var grantee = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, Guid.NewGuid()));
        await repo.AddAsync(Grant(grantee, Guid.NewGuid()));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsForGranteeAsync(grantee);

        active.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetActiveGrantsForGranteeAsync_DifferentGranteeOrganization_IsExcluded()
    {
        // Non-leakage: a grant issued to a different grantee organization must not leak.
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        await repo.AddAsync(Grant(Guid.NewGuid(), Guid.NewGuid()));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsForGranteeAsync(Guid.NewGuid());

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveGrantsForGranteeAsync_InactiveGrant_IsExcluded()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        var grantee = Guid.NewGuid();
        await repo.AddAsync(Grant(grantee, Guid.NewGuid(), isActive: false));
        await repo.SaveChangesAsync();

        var active = await repo.GetActiveGrantsForGranteeAsync(grantee);

        active.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_QueuesASyncOutboxEntrySnapshottingTheGrant()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            DateTimeOffset.Parse("2026-11-11T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var repo = new EFOrganizationGrantRepository(db, timeProvider);
        var granteeOrg = Guid.NewGuid();
        var resourceOrg = Guid.NewGuid();
        var grant = Grant(granteeOrg, resourceOrg);
        grant.ResourceType = "ServiceOrder";
        grant.ScopeId = "municipio-123";

        await repo.AddAsync(grant);
        await repo.SaveChangesAsync();

        var batch = await repo.GetSyncBatchAsync(afterSequence: 0, take: 10);

        batch.Should().ContainSingle();
        var entry = batch[0];
        entry.GrantId.Should().Be(grant.Id);
        entry.GranteeOrganizationId.Should().Be(granteeOrg);
        entry.ResourceOrganizationId.Should().Be(resourceOrg);
        entry.ResourceType.Should().Be("ServiceOrder");
        entry.ScopeId.Should().Be("municipio-123");
        entry.IsActive.Should().BeTrue();
        entry.SequenceNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateAsync_RevokingAGrant_QueuesANewSyncOutboxEntryReflectingRevocation()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        var grant = Grant(Guid.NewGuid(), Guid.NewGuid());
        await repo.AddAsync(grant);
        await repo.SaveChangesAsync();

        grant.IsActive = false;
        await repo.UpdateAsync(grant);
        await repo.SaveChangesAsync();

        var batch = await repo.GetSyncBatchAsync(afterSequence: 0, take: 10);

        batch.Should().HaveCount(2);
        batch[0].IsActive.Should().BeTrue();
        batch[1].IsActive.Should().BeFalse();
        batch[1].GrantId.Should().Be(grant.Id);
    }

    [Fact]
    public async Task GetSyncBatchAsync_RespectsAfterSequenceAndTake()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = NewContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repo = new EFOrganizationGrantRepository(db, TimeProvider.System);
        for (var i = 0; i < 5; i++)
        {
            await repo.AddAsync(Grant(Guid.NewGuid(), Guid.NewGuid()));
        }
        await repo.SaveChangesAsync();

        var firstPage = await repo.GetSyncBatchAsync(afterSequence: 0, take: 2);
        firstPage.Should().HaveCount(2);

        var secondPage = await repo.GetSyncBatchAsync(afterSequence: firstPage[^1].SequenceNumber, take: 10);
        secondPage.Should().HaveCount(3);
        secondPage.Select(e => e.SequenceNumber).Should().BeInAscendingOrder();
    }
}
