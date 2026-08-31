using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

/// <summary>
/// Exercises <see cref="InvitationRedirectGate"/> directly (XD01-89) — proves the
/// pending-invitation redirect check works standalone, independent of
/// <see cref="UserProvisioningService"/>/<c>Identity:Backend=InMemory</c>, which is the whole
/// point of the extraction: this is what actually runs under
/// <c>Identity:Backend=Rest</c>, where <c>UserProvisioningService</c> is never registered.
/// Mirrors the redirect-gate subset of <c>UserProvisioningServiceTests</c> one-for-one, minus
/// the provisioning-specific cases (this class has no user-provisioning responsibility).
/// </summary>
public class InvitationRedirectGateTests
{
    private sealed class FakeCurrentUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? GetCurrentUser() => user;
        public Task<CurrentUser?> GetCurrentUserAsync(CancellationToken ct = default) => Task.FromResult(user);
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

    private static (InvitationRedirectGate Sut, PendingInvitationStore Store, TestNavigationManager Navigation) BuildSut(
        CurrentUser? currentUser, bool registerInvitationRepo = true, string initialUri = "https://localhost/")
    {
        var store    = new PendingInvitationStore();
        var services = new ServiceCollection();
        if (registerInvitationRepo)
            services.AddSingleton<IPendingInvitationRepository>(new StorePendingInvitationRepository(store));
        var provider = services.BuildServiceProvider();

        var navigation = new TestNavigationManager(initialUri);
        var sut = new InvitationRedirectGate(
            new FakeCurrentUserAccessor(currentUser),
            provider,
            navigation);

        return (sut, store, navigation);
    }

    /// <summary>Minimal in-memory <see cref="IPendingInvitationRepository"/> — this test file
    /// only exercises the read-by-external-object-id path the gate itself calls, so the other
    /// members simply aren't needed here (unlike <c>InMemoryPendingInvitationRepository</c>,
    /// which backs the full admin invite UI).</summary>
    private sealed class PendingInvitationStore
    {
        public List<PendingInvitation> Invitations { get; } = [];
    }

    private sealed class StorePendingInvitationRepository(PendingInvitationStore store) : IPendingInvitationRepository
    {
        public Task<PendingInvitation?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken ct = default) =>
            Task.FromResult(store.Invitations.FirstOrDefault(i => i.ExternalObjectId == externalObjectId));

        public Task<PendingInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PendingInvitation>> GetAllPendingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(PendingInvitation invitation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_PendingInvitationExists_RedirectsToCompleteProfile()
    {
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.Invitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().ContainSingle(uri => uri.EndsWith("/complete-profile"));
    }

    [Fact]
    public async Task RedirectIfPendingAsync_NoInvitationAtAll_DoesNotRedirect()
    {
        var (sut, _, navigation) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []));

        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_InvitationAlreadyRedeemed_DoesNotRedirect()
    {
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.Invitations.Add(new PendingInvitation
        {
            ExternalObjectId = "user-3",
            Status           = InvitationStatus.Redeemed,
            RedeemedAt       = DateTime.UtcNow,
        });

        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_InvitationRevoked_DoesNotRedirect()
    {
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.Invitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Revoked });

        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_CalledTwice_RedirectsOnEveryCallUntilRedeemed()
    {
        // Proves the gate is a live check, not a one-shot flag that silently disarms itself
        // after firing once — same guarantee UserProvisioningServiceTests already proves for
        // the delegating (InMemory) path.
        var (sut, store, navigation) = BuildSut(new CurrentUser("user-3", "c@example.com", "Cid", []));
        store.Invitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.RedirectIfPendingAsync();
        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().HaveCount(2);
    }

    [Fact]
    public async Task RedirectIfPendingAsync_AlreadyOnCompleteProfilePage_DoesNotRedirectAgain()
    {
        var (sut, store, navigation) = BuildSut(
            new CurrentUser("user-3", "c@example.com", "Cid", []),
            initialUri: "https://localhost/complete-profile");
        store.Invitations.Add(new PendingInvitation { ExternalObjectId = "user-3", Status = InvitationStatus.Pending });

        await sut.RedirectIfPendingAsync();

        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_NoInvitationRepositoryRegistered_DoesNotThrowOrRedirect()
    {
        // Under Rest, IPendingInvitationRepository is always registered (XD01-70) — this proves
        // the gate degrades safely regardless, matching UserProvisioningService's existing
        // safety net for the same dependency rather than assuming it's always present.
        var (sut, _, navigation) = BuildSut(new CurrentUser("user-1", "a@example.com", "Ada", []), registerInvitationRepo: false);

        var act = () => sut.RedirectIfPendingAsync();

        await act.Should().NotThrowAsync();
        navigation.NavigatedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task RedirectIfPendingAsync_NoCurrentUser_DoesNotThrowOrRedirect()
    {
        var (sut, _, navigation) = BuildSut(currentUser: null);

        var act = () => sut.RedirectIfPendingAsync();

        await act.Should().NotThrowAsync();
        navigation.NavigatedTo.Should().BeEmpty();
    }
}
