using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Repositories;
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
/// </summary>
public class UserProvisioningServiceTests
{
    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
        public Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default) => Task.FromResult(user);
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>
    /// Invokes the private <c>ProvisionAsync</c> directly instead of going through the
    /// <c>AuthenticationStateChanged</c> event — that handler is <c>async void</c>, which has
    /// no reliable completion signal to await from a test.
    /// </summary>
    private static Task InvokeProvisionAsync(UserProvisioningService sut)
    {
        var method = typeof(UserProvisioningService).GetMethod(
            "ProvisionAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(sut, [new ClaimsPrincipal(new ClaimsIdentity())])!;
    }

    private static (UserProvisioningService Sut, WasmIdentityStore Store) BuildSut(CurrentUser currentUser)
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
    public async Task ProvisionAsync_NewUser_DoesNotAssignAnyRole()
    {
        var (sut, store) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await InvokeProvisionAsync(sut);

        store.Users.Should().ContainSingle(u => u.AzureObjectId == "user-1");
        store.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_UserWithExternalRolesInToken_StillAssignsNoLocalRole()
    {
        // Non-leakage in the other direction: even a user whose token already carries roles
        // must not additionally get a local UserRole row — that assignment path is gone
        // entirely, not just its default-role special case.
        var (sut, store) = BuildSut(new CurrentUser("user-2", "b@example.com", "Bob", ["Supervisor"]));

        await InvokeProvisionAsync(sut);

        store.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisionAsync_AlreadyProvisionedUser_DoesNotDuplicateUser()
    {
        var (sut, store) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await InvokeProvisionAsync(sut);
        await InvokeProvisionAsync(sut);

        store.Users.Should().ContainSingle();
    }
}
