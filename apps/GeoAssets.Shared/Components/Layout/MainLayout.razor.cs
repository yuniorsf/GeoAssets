using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
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

        await EnsureProvisionedOrRedirectAsync();
        await ResolveTopbarIdentityAsync();

        _identityResolved = true;

        // Fire-and-forget: Save/Load in TopBar and ProjectPanel only come alive once a Project
        // is open, and nothing opened one automatically before this — the buttons looked
        // clickable but silently did nothing until a user discovered the Projects panel and
        // picked one manually. Not awaited so the map still renders immediately; TopBar and
        // ProjectPanel both already re-render off Session.CurrentChanged once this lands.
        _ = OpenDefaultProjectAsync();
    }

    /// <summary>
    /// Ensures the local user record exists before checking authorization. UserProvisioningService
    /// isn't registered by AddGeoIdentityRest (Rest handles provisioning server-side instead,
    /// XD01-88) — resolved optionally so this still works if it's ever registered again. When it
    /// isn't registered, the pending-invitation redirect check has no such restriction
    /// (InvitationRedirectGate, XD01-89) — called directly so an invited caller still gets
    /// redirected to /complete-profile. A failure here (e.g. no admin consent yet for the identity
    /// API scope) must not take down the map page itself — the redirect is a convenience, not a
    /// hard requirement to render this layout.
    /// </summary>
    private async Task EnsureProvisionedOrRedirectAsync()
    {
        var provisioning = ServiceProvider.GetService<UserProvisioningService>();
        if (provisioning is not null)
        {
            await provisioning.EnsureProvisionedAsync();
            return;
        }

        try
        {
            var redirectGate = ServiceProvider.GetRequiredService<InvitationRedirectGate>();
            await redirectGate.RedirectIfPendingAsync();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[MainLayout] Failed to check pending invitation: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the roles/organization name shown in the topbar. Optional — a failure here (e.g.
    /// the identity backend being unreachable) must not take down the map page itself.
    /// </summary>
    private async Task ResolveTopbarIdentityAsync()
    {
        try
        {
            var currentUser = await CurrentUserAccessor.GetCurrentUserAsync();
            _userRoles = currentUser?.ExternalRoles ?? [];

            // Resolved optionally so a host that doesn't register IOrganizationRepository for
            // some reason just shows the topbar without an org name instead of failing to render.
            var organizationRepo = ServiceProvider.GetService<IOrganizationRepository>();
            _organizationName = await ResolveOrganizationNameAsync(currentUser, organizationRepo, UserRepository);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[MainLayout] Failed to resolve topbar identity info: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the organization's default <see cref="ProjectKind.General"/> Project if none is
    /// already open. A no-op if a Project is already open (e.g. this layout instance survived
    /// navigation) or if the org has none yet. Like the optional identity/org-name resolution
    /// above, a failure here (token/network issue) must not take down the map page.
    /// </summary>
    private async Task OpenDefaultProjectAsync()
    {
        if (Session.Current is not null) return;

        try
        {
            var context = await AuthService.GetAuthorizationContextAsync();
            if (context.User.OrganizationId is not { } organizationId) return;

            var projects = await ProjectClient.GetByOrganizationAsync(organizationId);
            if (SelectDefaultProject(projects) is { } defaultProject)
                await Session.OpenAsync(defaultProject.Id);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[MainLayout] Failed to auto-open default Project: {ex.Message}");
        }
    }

    /// <summary>
    /// Picks the org's default Project out of every Project <see cref="IProjectClient.GetByOrganizationAsync"/>
    /// returned — the earliest-created <see cref="ProjectKind.General"/> one. Org-level General
    /// Projects are reliably a single row in practice (<c>DefaultProjectSeeder</c> is idempotent
    /// per org), but this stays defensive rather than assuming exactly one. Pure/static so it's
    /// directly unit-testable without a Blazor render tree — same reasoning as
    /// <see cref="ResolveOrganizationNameAsync"/> and <c>ProjectPanel.Categorize</c>.
    /// </summary>
    public static Project? SelectDefaultProject(IReadOnlyList<Project> projects) =>
        projects.Where(p => p.Kind == ProjectKind.General)
                .OrderBy(p => p.CreatedAt)
                .FirstOrDefault();

    /// <summary>
    /// Resolves the display name of the current user's organization for the topbar.
    /// Returns <c>null</c> whenever an org name can't be determined — no organization
    /// repository registered, no current user, or the user has no organization assigned yet.
    /// </summary>
    public static async Task<string?> ResolveOrganizationNameAsync(
        CurrentUser?             currentUser,
        IOrganizationRepository? organizationRepo,
        IUserRepository          userRepository,
        CancellationToken        ct = default)
    {
        ArgumentNullException.ThrowIfNull(userRepository);
        if (organizationRepo is null || currentUser is null) return null;

        var appUser = await userRepository.GetByExternalObjectIdAsync(currentUser.ExternalObjectId, ct);
        if (appUser?.OrganizationId is not { } organizationId) return null;

        var organization = await organizationRepo.GetByIdAsync(organizationId, ct);
        return organization?.Name;
    }

    private void SignOut() => AuthNav.NavigateToLogout();
}
