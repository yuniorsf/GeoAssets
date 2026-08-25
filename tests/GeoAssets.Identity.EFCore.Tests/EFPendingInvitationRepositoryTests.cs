using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Identity.EFCore.Tests;

public class EFPendingInvitationRepositoryTests
{
    private static GeoIdentityDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GeoIdentityDbContext>()
            .UseSqlite(connection)
            .Options;
        return new GeoIdentityDbContext(options);
    }

    private static PendingInvitation Invitation(
        string email = "invitee@example.com",
        string externalObjectId = "",
        InvitationStatus status = InvitationStatus.Pending) => new()
    {
        Email            = email,
        ExternalObjectId = string.IsNullOrEmpty(externalObjectId) ? Guid.NewGuid().ToString() : externalObjectId,
        InvitedByUserId  = Guid.NewGuid(),
        InvitedAt        = DateTime.UtcNow,
        Status           = status,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllScalarFields()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);
        var invitation = Invitation();

        await repo.AddAsync(invitation);
        await repo.SaveChangesAsync();
        var loaded = await repo.GetByIdAsync(invitation.Id);

        loaded.Should().NotBeNull();
        loaded!.Email.Should().Be(invitation.Email);
        loaded.ExternalObjectId.Should().Be(invitation.ExternalObjectId);
        loaded.InvitedByUserId.Should().Be(invitation.InvitedByUserId);
        loaded.Status.Should().Be(InvitationStatus.Pending);
        loaded.RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);

        var loaded = await repo.GetByIdAsync(Guid.NewGuid());

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_MatchingInvitation_IsReturned()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);
        var invitation = Invitation(externalObjectId: "external-oid-1");
        await repo.AddAsync(invitation);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByExternalObjectIdAsync("external-oid-1");

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(invitation.Id);
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_NoMatch_ReturnsNull()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);

        var loaded = await repo.GetByExternalObjectIdAsync("does-not-exist");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetAllPendingAsync_OnlyReturnsPendingStatus()
    {
        // Non-leakage: redeemed/revoked invitations must not surface through the
        // "pending" listing used to drive the invitations admin UI.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);
        await repo.AddAsync(Invitation(status: InvitationStatus.Pending));
        await repo.AddAsync(Invitation(status: InvitationStatus.Redeemed));
        await repo.AddAsync(Invitation(status: InvitationStatus.Revoked));
        await repo.SaveChangesAsync();

        var pending = await repo.GetAllPendingAsync();

        pending.Should().ContainSingle();
        pending[0].Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task UpdateAsync_ChangesStatusAndRedeemedAt()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);
        var invitation = Invitation();
        await repo.AddAsync(invitation);
        await repo.SaveChangesAsync();

        invitation.Status = InvitationStatus.Redeemed;
        invitation.RedeemedAt = DateTime.UtcNow;
        await repo.UpdateAsync(invitation);
        await repo.SaveChangesAsync();
        var loaded = await repo.GetByIdAsync(invitation.Id);

        loaded!.Status.Should().Be(InvitationStatus.Redeemed);
        loaded.RedeemedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_DuplicateExternalObjectId_ThrowsOnSaveChanges()
    {
        // The unique index on ExternalObjectId is the JIT-provisioning match key's integrity
        // guarantee — two invitations pointing at the same Graph user would make first-login
        // matching ambiguous.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = NewContext(connection);
        db.Database.EnsureCreated();
        var repo = new EFPendingInvitationRepository(db);
        await repo.AddAsync(Invitation(email: "first@example.com", externalObjectId: "duplicate-oid"));
        await repo.SaveChangesAsync();

        await repo.AddAsync(Invitation(email: "second@example.com", externalObjectId: "duplicate-oid"));
        var act = async () => await repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
