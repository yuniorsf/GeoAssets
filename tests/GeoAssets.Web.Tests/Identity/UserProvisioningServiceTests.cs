using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Identity.InMemory;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// Proves JIT provisioning no longer grants a default role (XD01-19) — role assignment is
/// sourced from the external provider's roles claim instead, consumed by
/// <c>GeoAuthorizationService</c> (see <c>GeoAssets.Identity.Tests</c> for that side) — and
/// (XD01-71) the redirect-to-/complete-profile gate for a caller with a pending invitation.
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

    /// <summary>Standard test double for NavigationManager — overrides NavigateToCore instead of
    /// actually navigating, since there's no real host to navigate in a unit test.</summary>
    private sealed class TestNavigationManager : NavigationManager
    {
        public List<string> NavigatedTo { get; } = [];

        public TestNavigationManager(string initialUri = "https://localhost/")
            => Initialize(initialUri, initialUri);

        protected override void NavigateToCore(string uri, NavigationOptions options) => NavigatedTo.Add(uri);
    }

    private static (UserProvisioningService Sut, WasmIdentityStore Store, TestNavigationManager Navigation) BuildSut(
        CurrentUser? currentUser, bool registerInvitationRepo = true, string initialUri = "https://localhost/")
    {
        var store = new WasmIdentityStore();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserAccessor>(new FakeCurrentUserAccessor(currentUser));
        services.AddSingleton<IUserRepository>(new InMemoryUserRepository(store, TimeProvider.System));
        if (registerInvitationRepo)
            services.AddSingleton<IPendingInvitationRepository>(new InMemoryPendingInvitationRepository(store));
        var provider = services.BuildServiceProvider();

        var navigation = new TestNavigationManager(initialUri);
        var sut = new UserProvisioningService(
            new TestAuthenticationStateProvider(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            navigation);

        return (sut, store, navigation);
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NewUser_DoesNotAssignAnyRole()
    {
        var (sut, store, _) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

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
        var (sut, store, _) = BuildSut(new CurrentUser("user-2", "b@example.com", "Bob", ["Supervisor"]));

        await sut.EnsureProvisionedAsync();

        store.UserRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_AlreadyProvisionedUser_DoesNotDuplicateUser()
    {
        var (sut, store, _) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await sut.EnsureProvisionedAsync();
        await sut.EnsureProvisionedAsync();

        store.Users.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NoCurrentUser_DoesNotThrowOrAddAnything()
    {
        var (sut, store, _) = BuildSut(currentUser: null);

        await sut.EnsureProvisionedAsync();

        store.Users.Should().BeEmpty();
    }

    // ── Redirect gate (XD01-59 Phase 3, XD01-71) ────────────────────────────

    [Fact]
    public async Task EnsureProvisionedAsync_PendingInvitationExists_RedirectsToCompleteProfile()
    {
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.PendingInvitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().ContainSingle(uri => uri.EndsWith("/complete-profile"));
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NoInvitationAtAll_DoesNotRedirect()
    {
        var (sut, _, navigation) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_InvitationAlreadyRedeemed_StopsRedirecting()
    {
        // The concrete acceptance-criterion case: redeeming an invitation must stop future
        // redirects for that same user.
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.PendingInvitations.Add(new PendingInvitation
        {
            ExternalObjectId = "user-3",
            Status           = InvitationStatus.Redeemed,
            RedeemedAt       = DateTime.UtcNow,
        });

        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_InvitationRevoked_DoesNotRedirect()
    {
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.PendingInvitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Revoked });

        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_PendingInvitation_RedirectsOnEveryCallUntilRedeemed()
    {
        // Proves the gate is a live check, not a one-shot flag that silently disarms itself
        // after firing once.
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.PendingInvitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.EnsureProvisionedAsync();
        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnsureProvisionedAsync_AlreadyOnCompleteProfilePage_DoesNotRedirectAgain()
    {
        var (sut, store, navigation) = BuildSut(
            new CurrentUser("user-3", "c@example.com", "Cid", []),
            initialUri: "https://localhost/complete-profile");
        store.PendingInvitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.EnsureProvisionedAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureProvisionedAsync_NoInvitationRepositoryRegistered_DoesNotThrowOrRedirect()
    {
        // The Rest backend doesn't register UserProvisioningService at all today (XD01-12), but
        // this proves the gate itself degrades safely if IPendingInvitationRepository is ever
        // simply absent from the container, rather than throwing.
        var (sut, _, navigation) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []), registerInvitationRepo: false);

        var act = () => sut.EnsureProvisionedAsync();

        await act.Should().NotThrowAsync();
        navigation.NavigatedTo.Should().BeEmpty();
    }
}
