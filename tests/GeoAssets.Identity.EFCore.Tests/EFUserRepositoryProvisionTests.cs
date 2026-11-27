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
/// XD01-475 — <see cref="EFUserRepository.ProvisionAsync"/>: a user provisioned just in time on a request that lost the
/// race to a concurrent first request of the same user (<c>IX_Users_ExternalObjectId</c>) gets the stored user, with
/// its own insert discarded; any other save failure still throws.
/// </summary>
public sealed class EFUserRepositoryProvisionTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(Now);

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _connection.Dispose();

    private GeoIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<GeoIdentityDbContext>().UseSqlite(_connection).Options);

    private EFUserRepository Sut(GeoIdentityDbContext db) => new(db, _time);

    private static AppUser NewUser(string externalObjectId, string displayName = "Ada") => new()
    {
        ExternalObjectId = externalObjectId,
        Email            = externalObjectId + "@example.com",
        DisplayName      = displayName,
        CreatedAt        = Now.UtcDateTime,
    };

    private async Task<int> CountAsync(string externalObjectId)
    {
        await using var db = NewContext();
        return await db.Users.CountAsync(u => u.ExternalObjectId == externalObjectId);
    }

    [Fact]
    public async Task ANewUser_IsStoredAndReturned()
    {
        await using var db = NewContext();
        var user = NewUser("oid-new");

        var provisioned = await Sut(db).ProvisionAsync(user);

        provisioned.Should().BeSameAs(user);
        (await CountAsync("oid-new")).Should().Be(1);
    }

    [Fact]
    public async Task LosingTheRace_ReturnsTheStoredUser_AndDiscardsItsOwnInsert()
    {
        // The winner: another request's context provisioned the same user first.
        await using (var winnerDb = NewContext())
        {
            await Sut(winnerDb).ProvisionAsync(NewUser("oid-raced", displayName: "Winner"));
        }

        await using var db = NewContext();
        var loser = NewUser("oid-raced", displayName: "Loser");

        var provisioned = await Sut(db).ProvisionAsync(loser);

        provisioned.Should().NotBeSameAs(loser);
        provisioned.ExternalObjectId.Should().Be("oid-raced");
        provisioned.DisplayName.Should().Be("Winner");
        db.ChangeTracker.Entries<AppUser>().Should().NotContain(e => e.State == EntityState.Added,
            "a lingering insert would be retried by the request's next save");
        await db.Invoking(d => d.SaveChangesAsync()).Should().NotThrowAsync();
        (await CountAsync("oid-raced")).Should().Be(1);
    }

    [Fact]
    public async Task AnotherSaveFailure_IsNotSwallowed()
    {
        // A primary-key collision under a different external object id: no stored user explains it, so it must throw.
        var existing = NewUser("oid-existing");
        await using (var seedDb = NewContext())
        {
            await Sut(seedDb).ProvisionAsync(existing);
        }

        await using var db = NewContext();
        var colliding = NewUser("oid-other");
        colliding.Id = existing.Id;

        var act = () => Sut(db).ProvisionAsync(colliding);

        await act.Should().ThrowAsync<DbUpdateException>();
        (await CountAsync("oid-other")).Should().Be(0);
    }
}
