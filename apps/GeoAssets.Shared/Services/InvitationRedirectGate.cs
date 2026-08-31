using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Checks whether the current caller has a <see cref="InvitationStatus.Pending"/>
/// <see cref="PendingInvitation"/> and, if so, redirects to <c>/complete-profile</c> —
/// extracted from <see cref="UserProvisioningService.ProvisionAsync"/>'s redirect gate
/// (XD01-71) so the check can also run under <c>Identity:Backend=Rest</c> (XD01-89), where
/// <see cref="UserProvisioningService"/> itself is never registered even though
/// <see cref="IPendingInvitationRepository"/> is fully functional there (XD01-70). This class
/// has no backend-specific logic of its own — only its call sites differ by backend:
/// <see cref="UserProvisioningService.ProvisionAsync"/> now delegates to
/// <see cref="RedirectIfPendingAsync"/> instead of duplicating this logic (InMemory), and each
/// admin/index page's <c>OnInitializedAsync</c> calls it directly when
/// <c>ServiceProvider.GetService&lt;UserProvisioningService&gt;()</c> is <c>null</c> (Rest).
///
/// Registered as scoped, unconditionally in <c>Program.cs</c> (not inside either
/// <c>GeoIdentityWasmExtensions.AddGeoIdentityWasmDev</c> or
/// <c>GeoIdentityRestExtensions.AddGeoIdentityRest</c>) — its dependencies
/// (<see cref="ICurrentUserAccessor"/>, <see cref="IPendingInvitationRepository"/>,
/// <see cref="NavigationManager"/>) are already registered under both backends today.
///
/// Resolves <see cref="IPendingInvitationRepository"/> optionally via <see cref="IServiceProvider"/>
/// rather than direct constructor injection, matching the pattern
/// <see cref="UserProvisioningService"/> already used for the same dependency: this must
/// degrade safely (no redirect, no throw) if that repository is ever absent from the
/// container, rather than assuming it's always registered.
/// </summary>
public sealed class InvitationRedirectGate(
    ICurrentUserAccessor currentUserAccessor,
    IServiceProvider     serviceProvider,
    NavigationManager    navigation)
{
    private const string CompleteProfilePath = "/complete-profile";

    /// <summary>
    /// Safe to call on every page load — a caller with no current user, no pending
    /// invitation, or no registered <see cref="IPendingInvitationRepository"/> simply returns
    /// without effect.
    /// </summary>
    public async Task RedirectIfPendingAsync(CancellationToken ct = default)
    {
        var current = await currentUserAccessor.GetCurrentUserAsync(ct);
        if (current is null || string.IsNullOrEmpty(current.ExternalObjectId)) return;

        var invitationRepo = serviceProvider.GetService<IPendingInvitationRepository>();
        if (invitationRepo is null) return;

        // Don't redirect to the page we might already be on — also guards against a redirect
        // loop if CompleteProfile.razor itself ever calls this.
        if (navigation.Uri.EndsWith(CompleteProfilePath, StringComparison.OrdinalIgnoreCase)) return;

        var invitation = await invitationRepo.GetByExternalObjectIdAsync(current.ExternalObjectId, ct);
        if (invitation is null || invitation.Status != InvitationStatus.Pending) return;

        navigation.NavigateTo(CompleteProfilePath);
    }
}
