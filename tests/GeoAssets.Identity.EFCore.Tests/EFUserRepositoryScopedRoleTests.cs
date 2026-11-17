using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

/// <summary>
/// Organization-scoped role assignments (XD01-325): a user may hold a role at home
/// (<c>OrganizationId</c> null) and the same role on another organization, once per scope, and
/// the pre-existing home-scoped reads must not start seeing the scoped rows.
/// </summary>
public sealed class EFUserRepositoryScopedRoleTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(Now);

    private Guid _homeOrg;
    private Guid _coelba;
    private Guid _thirdOrg;
    private Guid _userId;
    private Guid _otherUserId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        await GeoIdentitySeeder.SeedAsync(db, _time);

        var home = NewOrganization("bimbo");
        var coelba = NewOrganization("coelba");
        var third = NewOrganization("third");
        var user = NewUser("ada", home.Id);
        var other = NewUser("bob", home.Id);
        db.AddRange(home, coelba, third, user, other);
        await db.SaveChangesAsync();

        (_homeOrg, _coelba, _thirdOrg, _userId, _otherUserId) = (home.Id, coelba.Id, third.Id, user.Id, other.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);

    private EFUserRepository Sut(GeoIdentityDbContext db) => new(db, _time);

    private static Organization NewOrganization(string slug) =>
        new() { Name = slug, Slug = slug, CreatedAt = Now.UtcDateTime };

    private static AppUser NewUser(string name, Guid organizationId) => new()
    {
        ExternalObjectId = name,
        Email            = name + "@example.com",
        DisplayName      = name,
        OrganizationId   = organizationId,
        CreatedAt        = Now.UtcDateTime,
    };

    private async Task<List<(Guid RoleId, Guid? OrganizationId)>> StoredAssignmentsAsync(Guid userId)
    {
        await using var db = NewContext();
        var rows = await db.UserRoles.AsNoTracking().Where(ur => ur.UserId == userId).ToListAsync();
        return [.. rows.Select(ur => (ur.RoleId, ur.OrganizationId))];
    }

    // ── Assign ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameRole_CanBeHeldAtHomeAndOnAnotherOrganizationAtTheSameTime()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, "admin");
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba, "coelba-admin");
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _thirdOrg);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().BeEquivalentTo(new (Guid, Guid?)[]
        {
            (GeoIdentitySeeder.TechnicianRoleId, null),
            (GeoIdentitySeeder.TechnicianRoleId, _coelba),
            (GeoIdentitySeeder.TechnicianRoleId, _thirdOrg),
        });

        await using var read = NewContext();
        var scoped = await read.UserRoles.AsNoTracking().SingleAsync(ur => ur.OrganizationId == _coelba);
        scoped.Id.Should().NotBe(Guid.Empty);
        scoped.AssignedBy.Should().Be("coelba-admin");
        scoped.AssignedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task AssignRoleOnOrganizationAsync_SameUserRoleAndOrganizationTwice_IsIdempotent()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = NewContext();
            var sut = Sut(db);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().Equal((GeoIdentitySeeder.TechnicianRoleId, _coelba));
    }

    [Fact]
    public async Task AssignRoleAsync_SameUserAndRoleTwiceAtHome_IsIdempotent_EvenWithAScopedAssignmentOfTheSameRole()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        // The scoped row must not be mistaken for the home assignment (which would skip the
        // insert), and the second home assignment must not create a duplicate.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var db = NewContext();
            var sut = Sut(db);
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.TechnicianRoleId);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().BeEquivalentTo(new (Guid, Guid?)[]
        {
            (GeoIdentitySeeder.TechnicianRoleId, null),
            (GeoIdentitySeeder.TechnicianRoleId, _coelba),
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_RejectsASecondRowForTheSameUserRoleAndScope(bool scoped)
    {
        // Bypasses the repository's own existence check: the uniqueness must hold in the schema,
        // for the null (home) scope as much as for an organization.
        Guid? organizationId = scoped ? _coelba : null;
        await using var db = NewContext();
        db.UserRoles.Add(NewAssignment(organizationId));
        await db.SaveChangesAsync();

        db.UserRoles.Add(NewAssignment(organizationId));
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();

        UserRole NewAssignment(Guid? scope) => new()
        {
            UserId         = _userId,
            RoleId         = GeoIdentitySeeder.TechnicianRoleId,
            OrganizationId = scope,
            AssignedAt     = Now.UtcDateTime,
        };
    }

    // ── Revoke ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveRoleOnOrganizationAsync_RemovesOnlyThatScope()
    {
        await AssignEverywhereAsync();

        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.RemoveRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().BeEquivalentTo(new (Guid, Guid?)[]
        {
            (GeoIdentitySeeder.TechnicianRoleId, null),
            (GeoIdentitySeeder.TechnicianRoleId, _thirdOrg),
        });
    }

    [Fact]
    public async Task RemoveRoleAsync_RemovesOnlyTheHomeAssignment()
    {
        await AssignEverywhereAsync();

        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.RemoveRoleAsync(_userId, GeoIdentitySeeder.TechnicianRoleId);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().BeEquivalentTo(new (Guid, Guid?)[]
        {
            (GeoIdentitySeeder.TechnicianRoleId, _coelba),
            (GeoIdentitySeeder.TechnicianRoleId, _thirdOrg),
        });
    }

    [Fact]
    public async Task Remove_AssignmentThatDoesNotExist_IsANoOp()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var sut = Sut(db);
            // Held on coelba only: neither the home scope nor another organization has it.
            await sut.RemoveRoleAsync(_userId, GeoIdentitySeeder.TechnicianRoleId);
            await sut.RemoveRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _thirdOrg);
            await sut.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().Equal((GeoIdentitySeeder.TechnicianRoleId, _coelba));
    }

    // ── Home-scoped reads keep their meaning ────────────────────────────────

    [Fact]
    public async Task HomeScopedReads_IgnoreOrganizationScopedAssignments()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.ReadOnlyRoleId);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.AdminRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        await using var read = NewContext();
        var repo = Sut(read);
        var readOnlyPermissions = await new EFRoleRepository(read).GetPermissionsAsync(GeoIdentitySeeder.ReadOnlyRoleId);

        (await repo.GetRolesAsync(_userId)).Select(r => r.Id).Should().Equal(GeoIdentitySeeder.ReadOnlyRoleId);
        (await repo.GetEffectivePermissionsAsync(_userId)).Select(p => p.Code)
            .Should().BeEquivalentTo(readOnlyPermissions.Select(p => p.Code));
        (await repo.GetByRoleAsync("Administrator")).Should().BeEmpty(
            "holding Administrator on another organization does not make the user an Administrator");
        (await repo.GetByRoleAsync("ReadOnly")).Select(u => u.Id).Should().Equal(_userId);
    }

    // ── Listing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetScopedRoleAssignmentsAsync_ReturnsOnlyThatUsersScopedAssignments_WithRoleAndPermissions()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.AdminRoleId);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.ReadOnlyRoleId, _thirdOrg);
            await sut.AssignRoleOnOrganizationAsync(_otherUserId, GeoIdentitySeeder.SupervisorRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        await using var read = NewContext();
        var assignments = await Sut(read).GetScopedRoleAssignmentsAsync(_userId);

        assignments.Select(ur => (ur.OrganizationId, ur.Role.Name)).Should().BeEquivalentTo(new (Guid?, string)[]
        {
            (_coelba, "FieldTechnician"),
            (_thirdOrg, "ReadOnly"),
        });
        var technician = assignments.Single(ur => ur.OrganizationId == _coelba).Role;
        technician.RolePermissions.Select(rp => rp.Permission.Code)
            .Should().BeEquivalentTo((await new EFRoleRepository(read).GetPermissionsAsync(GeoIdentitySeeder.TechnicianRoleId)).Select(p => p.Code))
            .And.NotBeEmpty();
    }

    [Fact]
    public async Task GetScopedRoleAssignmentsAsync_UserWithOnlyHomeAssignments_ReturnsEmpty()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.AdminRoleId);
            await sut.SaveChangesAsync();
        }

        await using var read = NewContext();
        (await Sut(read).GetScopedRoleAssignmentsAsync(_userId)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoleAssignmentsOnOrganizationAsync_ReturnsAssignmentsScopedToThatOrganizationOnly_WithUserAndRole()
    {
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            // Home assignments of users who *belong* to an organization are not assignments "on" it.
            await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.AdminRoleId);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
            await sut.AssignRoleOnOrganizationAsync(_otherUserId, GeoIdentitySeeder.SupervisorRoleId, _coelba);
            await sut.AssignRoleOnOrganizationAsync(_otherUserId, GeoIdentitySeeder.ReadOnlyRoleId, _thirdOrg);
            await sut.SaveChangesAsync();
        }

        await using var read = NewContext();
        var repo = Sut(read);

        (await repo.GetRoleAssignmentsOnOrganizationAsync(_coelba))
            .Select(ur => (ur.User.DisplayName, ur.Role.Name))
            .Should().BeEquivalentTo(new[] { ("ada", "FieldTechnician"), ("bob", "Supervisor") });
        (await repo.GetRoleAssignmentsOnOrganizationAsync(_homeOrg)).Should().BeEmpty();
    }

    // ── Cascades ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletingARole_RemovesItsScopedAndHomeAssignments()
    {
        await AssignEverywhereAsync();
        await using (var db = NewContext())
        {
            var sut = Sut(db);
            await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.ReadOnlyRoleId, _coelba);
            await sut.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            var roles = new EFRoleRepository(db);
            await roles.DeleteAsync(GeoIdentitySeeder.TechnicianRoleId);
            await roles.SaveChangesAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().Equal((GeoIdentitySeeder.ReadOnlyRoleId, _coelba));
    }

    [Fact]
    public async Task DeletingAnOrganization_RemovesTheAssignmentsScopedToIt_AndNothingElse()
    {
        await AssignEverywhereAsync();

        await using (var db = NewContext())
        {
            // ExecuteDelete issues a bare DELETE: the cleanup has to come from the foreign key,
            // not from EF cascading over entities it happens to be tracking.
            (await db.Organizations.Where(o => o.Id == _coelba).ExecuteDeleteAsync()).Should().Be(1);
        }

        (await StoredAssignmentsAsync(_userId)).Should().BeEquivalentTo(new (Guid, Guid?)[]
        {
            (GeoIdentitySeeder.TechnicianRoleId, null),
            (GeoIdentitySeeder.TechnicianRoleId, _thirdOrg),
        });
    }

    [Fact]
    public async Task DeletingTheUsersHomeOrganization_KeepsTheirHomeScopedAssignments()
    {
        // Users.OrganizationId is ON DELETE SET NULL; a home assignment (OrganizationId null on
        // the assignment itself) is not tied to that organization row and must survive.
        await AssignEverywhereAsync();

        await using (var db = NewContext())
        {
            await db.Organizations.Where(o => o.Id == _homeOrg).ExecuteDeleteAsync();
        }

        (await StoredAssignmentsAsync(_userId)).Should().HaveCount(3);
    }

    private async Task AssignEverywhereAsync()
    {
        await using var db = NewContext();
        var sut = Sut(db);
        await sut.AssignRoleAsync(_userId, GeoIdentitySeeder.TechnicianRoleId);
        await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _coelba);
        await sut.AssignRoleOnOrganizationAsync(_userId, GeoIdentitySeeder.TechnicianRoleId, _thirdOrg);
        await sut.SaveChangesAsync();
    }
}
