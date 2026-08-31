using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Components.Layout;

public partial class MainLayout
{
    private string _userDisplayName = string.Empty;
    private string? _organizationName;
    private IReadOnlyList<string> _userRoles = [];

    private bool _isMapRoute;

    // Gates @Body/NavMenu until identity/provisioning resolution below completes — Blazor does
    // not guarantee this layout's OnInitializedAsync finishes before a routed page's own
    // OnInitializedAsync starts, so a page relying on resolved identity/roles could otherwise
    // run before it's ready.
    private bool _identityResolved;

    // ─── Route tracking ───────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        // Recomputed on every navigation (Body's RenderFragment changing triggers this) rather
        // than a one-time OnInitialized check, since this layout instance persists across routes.
        _isMapRoute = Nav.ToBaseRelativePath(Nav.Uri).TrimEnd('/').Length == 0;
    }

    // ─── Init ─────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        // Force-resolve — Scoped services aren't auto-constructed; this is what actually makes
        // it start listening to IProviderPool.EntryAdded and rendering newly-connected
        // providers onto the map (XD01-83), replacing the old @ref-based boot-flow wiring.
        ServiceProvider.GetRequiredService<ProviderConnectionMapRenderer>();

        var state = await AuthStateProvider.GetAuthenticationStateAsync();
        var user  = state.User;
        _userDisplayName = user.FindFirst("name")?.Value
                        ?? user.FindFirst("preferred_username")?.Value
                        ?? user.Identity?.Name
                        ?? L["app.defaultUser"];

        // Ensure the local user record exists before checking authorization. Only relevant
        // under Identity:Backend=InMemory (Rest has no local provisioning step, so the service
        // isn't registered there — resolved optionally to avoid breaking that backend).
        var provisioning = ServiceProvider.GetService<UserProvisioningService>();
        if (provisioning is not null)
        {
            await provisioning.EnsureProvisionedAsync();
        }
        else
        {
            // Rest backend: UserProvisioningService isn't registered (no local provisioning
            // step), but the pending-invitation redirect check has no such restriction
            // (InvitationRedirectGate, XD01-89) — call it directly so an invited caller still
            // gets redirected to /complete-profile.
            try
            {
                var redirectGate = ServiceProvider.GetRequiredService<InvitationRedirectGate>();
                await redirectGate.RedirectIfPendingAsync();
            }
            catch (Exception ex)
            {
                // The redirect is a convenience, not a hard requirement to render this layout —
                // a token/network failure here (e.g. no admin consent yet for the identity API
                // scope) must not take down the map page itself.
                Console.Error.WriteLine($"[MainLayout] Failed to check pending invitation: {ex.Message}");
            }
        }

        try
        {
            var currentUser = await CurrentUserAccessor.GetCurrentUserAsync();
            _userRoles = currentUser?.ExternalRoles ?? [];

            // IOrganizationRepository is only registered under Identity:Backend=InMemory
            // (see GeoIdentityRestExtensions) — resolved optionally so the Rest backend,
            // which has no org endpoint yet, just shows the topbar without an org name.
            var organizationRepo = ServiceProvider.GetService<IOrganizationRepository>();
            _organizationName = await ResolveOrganizationNameAsync(currentUser, organizationRepo, UserRepository);
        }
        catch (Exception ex)
        {
            // Organization/role display in the topbar is optional — a failure here (e.g. the
            // identity backend being unreachable) must not take down the map page itself.
            Console.Error.WriteLine($"[MainLayout] Failed to resolve topbar identity info: {ex.Message}");
        }

        _identityResolved = true;
    }

    /// <summary>
    /// Resolves the display name of the current user's organization for the topbar.
    /// Returns <c>null</c> whenever an org name can't be determined — no organization
    /// repository registered (e.g. Identity:Backend=Rest, XD01-77 tracks a real org
    /// endpoint there), no current user, or the user has no organization assigned yet.
    /// </summary>
    public static async Task<string?> ResolveOrganizationNameAsync(
        CurrentUser?             currentUser,
        IOrganizationRepository? organizationRepo,
        IUserRepository          userRepository,
        CancellationToken        ct = default)
    {
        if (organizationRepo is null || currentUser is null) return null;

        var appUser = await userRepository.GetByExternalObjectIdAsync(currentUser.ExternalObjectId, ct);
        if (appUser?.OrganizationId is not { } organizationId) return null;

        var organization = await organizationRepo.GetByIdAsync(organizationId, ct);
        return organization?.Name;
    }

    private void SignOut() => AuthNav.NavigateToLogout();
}
