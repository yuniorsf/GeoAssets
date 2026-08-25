using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Identity.InMemory;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// Proves JIT provisioning no longer grants a default role (XD01-19) — role assignment is
/// sourced from the external provider's roles claim instead, consumed by
/// <c>GeoAuthorizationService</c> (see <c>GeoAssets.Identity.Tests</c> for that side).
///
/// Exercises the public <c>EnsureProvisionedAsync</c> path directly rather than the reactive
/// <c>AuthenticationStateChanged</c> subscription — that handler is <c>async void</c>, which
/// has no reliable completion signal to await from a test, and per its own doc comment is not
/// the path a caller who actually needs the user to exist should rely on anyway.
/// </summary>
public class UserProvisioningServiceTests
{
    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
        public Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default) => Task.FromResult(user);
    }

    private sealed class TestAuthenticationStateProvider(ClaimsPrincipal? principal = null) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal ?? new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static (UserProvisioningService Sut, WasmIdentityStore Store) BuildSut(CurrentUser? currentUser)
    {
        var store = new WasmIdentityStore();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserAccessor>(new FakeCurrentUserAccessor(currentUser));
        services.AddSingleton<IUserRepository>(new InMemoryUserRepository(store, TimeProvider.System));
        var provider = services.BuildServiceProvider();

        var sut = new UserProvisioningService(
            new TestAuthenticationStateProvider(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);

        return (sut, store);
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NewUser_DoesNotAssignAnyRole()
    {
        var (sut, store) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await sut.EnsureProvisionedAsync();

        store.Users.Should().ContainSingle(u => u.ExternalObjectId == "user-1");
        store.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_UserWithExternalRolesInToken_StillAssignsNoLocalRole()
    {
        // Non-leakage in the other direction: even a user whose token already carries roles
        // must not additionally get a local UserRole row — that assignment path is gone
        // entirely, not just its default-role special case.
        var (sut, store) = BuildSut(new CurrentUser("user-2", "b@example.com", "Bob", ["Supervisor"]));

        await sut.EnsureProvisionedAsync();

        store.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_AlreadyProvisionedUser_DoesNotDuplicateUser()
    {
        var (sut, store) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await sut.EnsureProvisionedAsync();
        await sut.EnsureProvisionedAsync();

        store.Users.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NoCurrentUser_DoesNotThrowOrAddAnything()
    {
        var (sut, store) = BuildSut(currentUser: null);

        await sut.EnsureProvisionedAsync();

        store.Users.Should().BeEmpty();
    }
}
