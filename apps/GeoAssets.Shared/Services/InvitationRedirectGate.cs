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
/// (XD01-71/XD01-89) so the check runs regardless of whether
/// <see cref="UserProvisioningService"/> itself is registered (XD01-130: it currently never
/// is). This class has no backend-specific logic of its own:
/// <see cref="UserProvisioningService.ProvisionAsync"/> delegates to
/// <see cref="RedirectIfPendingAsync"/> instead of duplicating this logic, and each
/// admin/index page's <c>OnInitializedAsync</c> calls it directly when
/// <c>ServiceProvider.GetService&lt;UserProvisioningService&gt;()</c> is <c>null</c> — the
/// case today.
///
/// Registered as scoped, unconditionally in <c>Program.cs</c> (not inside
/// <c>GeoIdentityRestExtensions.AddGeoIdentityRest</c>) — its dependencies
/// (<see cref="ICurrentUserAccessor"/>, <see cref="IPendingInvitationRepository"/>,
/// <see cref="NavigationManager"/>) are already registered there.
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
