using System.Collections.Concurrent;
using FluentAssertions;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// XD01-475 against PostgreSQL's real <c>IX_Users_ExternalObjectId</c>: a new user's first map view sends a burst of
/// requests that all provision the user just in time. Exactly one row is stored, and every request gets that user —
/// none fails with the unique violation that used to answer 500.
/// </summary>
public sealed class JitUserProvisioningConcurrencyTests
{
    private const int Requests = 24;

    [Fact]
    public async Task ABurstOfFirstRequests_StoresOneUser_AndEveryRequestGetsIt()
    {
        var connectionString = await SecurityTestContainerFixture.EnsureStartedAsync();
        var options = SecurityTestContainerFixture.BuildGeoIdentityOptions(connectionString);
        var oid = Guid.NewGuid().ToString(); // an Entra object id: 36 characters, the column's width

        var provisioned = new ConcurrentBag<Guid>();
        var failures = new ConcurrentBag<Exception>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, Requests).Select(_ => Task.Run(async () =>
        {
            await gate.Task;
            try
            {
                await using var db = new GeoIdentityDbContext(options);
                IUserRepository repository = new EFUserRepository(db, TimeProvider.System); // as GeoAuthorizationService calls it
                var user = await repository.ProvisionAsync(new AppUser
                {
                    ExternalObjectId = oid,
                    Email            = oid + "@example.com",
                    DisplayName      = "Burst",
                    CreatedAt        = TimeProvider.System.GetUtcNow().UtcDateTime,
                });
                provisioned.Add(user.Id);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        })).ToList();

        gate.SetResult();
        await Task.WhenAll(requests);

        failures.Should().BeEmpty();
        await using var check = new GeoIdentityDbContext(options);
        var stored = await check.Users.AsNoTracking().Where(u => u.ExternalObjectId == oid).Select(u => u.Id).ToListAsync();
        stored.Should().ContainSingle();
        provisioned.Should().HaveCount(Requests).And.OnlyContain(id => id == stored[0]);
    }
}
