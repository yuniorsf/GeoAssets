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
/// The scoped role assignment feed (XD01-328): every change to what a user holds on another organization — whichever write
/// path makes it — is recorded in the same save (<c>ScopedRoleAssignmentChangeCapture</c>) and served by
/// <see cref="EFScopedRoleAssignmentSyncFeed"/> as the pair's current state, a revocation as a tombstone.
/// </summary>
public sealed class ScopedRoleAssignmentSyncFeedTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(Now);

    private Guid _bimbo;
    private Guid _coelba;
    private Guid _userId;
    private Guid _contractorRoleId;
    private Guid _viewPermissionId;
    private Guid _executePermissionId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();

        var bimbo = new Organization { Name = "bimbo", Slug = "bimbo", CreatedAt = Now.UtcDateTime };
        var coelba = new Organization { Name = "coelba", Slug = "coelba", CreatedAt = Now.UtcDateTime };
        var user = new AppUser { ExternalObjectId = "oid-ada", Email = "ada@bimbo", DisplayName = "Ada", OrganizationId = bimbo.Id, CreatedAt = Now.UtcDateTime };
        var view = new AppPermission { Code = "serviceorders:view", Description = "view" };
        var execute = new AppPermission { Code = "serviceorders:execute", Description = "execute" };
        var role = new AppRole { Name = "Contractor C1", Description = "contractor" };
        role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = view.Id });
        db.AddRange(bimbo, coelba, user, view, execute, role);
        await db.SaveChangesAsync();

        (_bimbo, _coelba, _userId, _contractorRoleId, _viewPermissionId, _executePermissionId) =
            (bimbo.Id, coelba.Id, user.Id, role.Id, view.Id, execute.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);

    private async Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> FeedAsync(long after = 0, int take = 100)
    {
        await using var db = NewContext();
        return await new EFScopedRoleAssignmentSyncFeed(db).GetSyncBatchAsync(after, take);
    }

    private async Task AssignOnCoelbaAsync()
    {
        await using var db = NewContext();
        var users = new EFUserRepository(db, _time);
        await users.AssignRoleOnOrganizationAsync(_userId, _contractorRoleId, _coelba, "coelba-admin");
        await users.SaveChangesAsync();
    }

    [Fact]
    public async Task AssigningARoleOnAnotherOrganization_IsFedAsWhatTheUserHoldsThere()
    {
        await AssignOnCoelbaAsync();

        var entry = (await FeedAsync()).Should().ContainSingle().Subject;
        entry.UserId.Should().Be(_userId);
        entry.ExternalObjectId.Should().Be("oid-ada");
        entry.ResourceOrganizationId.Should().Be(_coelba);
        entry.RoleNames.Should().Equal("Contractor C1");
        entry.PermissionCodes.Should().Equal("serviceorders:view");
        entry.IsActive.Should().BeTrue();
        entry.SequenceNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RevokingTheAssignment_IsFedAsATombstone_AndEveryEarlierEntryNowResolvesToIt()
    {
        await AssignOnCoelbaAsync();
        await using (var db = NewContext())
        {
            var users = new EFUserRepository(db, _time);
            await users.RemoveRoleOnOrganizationAsync(_userId, _contractorRoleId, _coelba);
            await users.SaveChangesAsync();
        }

        var batch = await FeedAsync();

        batch.Should().HaveCount(2);
        batch.Should().OnlyContain(e => !e.IsActive && e.RoleNames.Count == 0 && e.PermissionCodes.Count == 0 && e.ResourceOrganizationId == _coelba);
        batch[1].SequenceNumber.Should().BeGreaterThan(batch[0].SequenceNumber);
    }

    [Fact]
    public async Task AHomeRoleAssignment_IsNotFed()
    {
        await using (var db = NewContext())
        {
            var users = new EFUserRepository(db, _time);
            await users.AssignRoleAsync(_userId, _contractorRoleId);
            await users.SaveChangesAsync();
        }

        (await FeedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ARoleOnTheUsersOwnHomeOrganization_CountsForNothing_AsInTheAuthorizationContext()
    {
        await using (var db = NewContext())
        {
            var users = new EFUserRepository(db, _time);
            await users.AssignRoleOnOrganizationAsync(_userId, _contractorRoleId, _bimbo);
            await users.SaveChangesAsync();
        }

        var entry = (await FeedAsync()).Should().ContainSingle().Subject;
        entry.ResourceOrganizationId.Should().Be(_bimbo);
        entry.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GrantingThatRoleAnotherPermission_FedsTheAssignmentAgainWithIt()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            var roles = new EFRoleRepository(db);
            await roles.GrantPermissionAsync(_contractorRoleId, _executePermissionId);
            await roles.SaveChangesAsync();
        }

        var entry = (await FeedAsync(after: first)).Should().ContainSingle().Subject;
        entry.PermissionCodes.Should().BeEquivalentTo("serviceorders:view", "serviceorders:execute");
        entry.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RevokingThatRolesLastPermission_KeepsTheRole_WithoutTheCode()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            var roles = new EFRoleRepository(db);
            await roles.RevokePermissionAsync(_contractorRoleId, _viewPermissionId);
            await roles.SaveChangesAsync();
        }

        var entry = (await FeedAsync(after: first)).Should().ContainSingle().Subject;
        entry.RoleNames.Should().Equal("Contractor C1");
        entry.PermissionCodes.Should().BeEmpty();
        entry.IsActive.Should().BeTrue("the user still holds the role there, which a grant's RequiredRole may name");
    }

    [Fact]
    public async Task DeletingTheRole_FedsATombstone()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            var roles = new EFRoleRepository(db);
            await roles.DeleteAsync(_contractorRoleId);
            await roles.SaveChangesAsync();
        }

        var entry = (await FeedAsync(after: first)).Should().ContainSingle().Subject;
        entry.ResourceOrganizationId.Should().Be(_coelba);
        entry.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeletingTheResourceOrganization_FedsATombstone()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            db.Organizations.Remove(await db.Organizations.SingleAsync(o => o.Id == _coelba));
            await db.SaveChangesAsync();
        }

        var entry = (await FeedAsync(after: first)).Should().ContainSingle().Subject;
        entry.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task TheUsersIdentityProviderIdChanging_FedsTheAssignmentAgainUnderTheNewId()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == _userId);
            user.ExternalObjectId = "oid-ada-2";
            await db.SaveChangesAsync();
        }

        var entry = (await FeedAsync(after: first)).Should().ContainSingle().Subject;
        entry.ExternalObjectId.Should().Be("oid-ada-2");
        entry.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnrelatedChangeToTheUser_FedsNothing()
    {
        await AssignOnCoelbaAsync();
        var first = (await FeedAsync()).Single().SequenceNumber;

        await using (var db = NewContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == _userId);
            user.LastLoginAt = Now.UtcDateTime.AddHours(1);
            user.DisplayName = "Ada L.";
            await db.SaveChangesAsync();
        }

        (await FeedAsync(after: first)).Should().BeEmpty();
    }

    [Fact]
    public async Task ASynchronousSave_IsRecordedToo()
    {
        AssignOnCoelbaSynchronously();

        (await FeedAsync()).Should().ContainSingle().Which.IsActive.Should().BeTrue();
    }

    private void AssignOnCoelbaSynchronously()
    {
        using var db = NewContext();
        db.UserRoles.Add(new UserRole { UserId = _userId, RoleId = _contractorRoleId, OrganizationId = _coelba, AssignedAt = Now.UtcDateTime });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetSyncBatchAsync_RespectsAfterSequenceAndTake()
    {
        await AssignOnCoelbaAsync();
        await using (var db = NewContext())
        {
            var users = new EFUserRepository(db, _time);
            await users.RemoveRoleOnOrganizationAsync(_userId, _contractorRoleId, _coelba);
            await users.SaveChangesAsync();
        }
        await AssignOnCoelbaAsync();

        var firstPage = await FeedAsync(after: 0, take: 2);
        var secondPage = await FeedAsync(after: firstPage[^1].SequenceNumber, take: 10);

        firstPage.Should().HaveCount(2);
        secondPage.Should().ContainSingle().Which.IsActive.Should().BeTrue();
        secondPage[0].SequenceNumber.Should().BeGreaterThan(firstPage[^1].SequenceNumber);
        (await FeedAsync(after: secondPage[0].SequenceNumber)).Should().BeEmpty();
    }
}
