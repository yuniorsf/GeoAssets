using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Provisions a local <see cref="AppUser"/> the first time a user authenticates
/// (Just-In-Time provisioning) — both reactively, via
/// <see cref="AuthenticationStateProvider.AuthenticationStateChanged"/>, and on demand via
/// <see cref="EnsureProvisionedAsync"/>.
///
/// The reactive path alone is unreliable for anything that needs the user to already exist
/// by the time it runs: <c>AuthenticationStateChanged</c> only fires on a *transition*, so a
/// user resuming a cached MSAL session on a plain reload (not a fresh interactive login) may
/// never raise it — and the WASM in-memory identity store is pure in-memory, so it resets on
/// every reload regardless. A page whose <c>OnInitializedAsync</c> needs the user to already
/// be provisioned (e.g. before checking <c>IGeoAuthorizationService.HasPermissionAsync</c>)
/// should call <see cref="EnsureProvisionedAsync"/> explicitly and await it — safe to do from
/// a routed <c>[Authorize]</c> page, since <c>AuthorizeRouteView</c> only renders it once
/// authentication has already resolved, so there's no race there.
///
/// No default role is granted (XD01-19) — role assignment is sourced from the external
/// provider's roles claim (<see cref="CurrentUser.ExternalRoles"/>), consumed by
/// <see cref="GeoAssets.Identity.Authorization.Services.GeoAuthorizationService"/>. A newly
/// provisioned user with no external role assignment gets a safe empty <c>Roles</c> list —
/// see <see cref="GeoAssets.Identity.Authorization.Services.AuthorizationContext"/>.
///
/// Registered as a singleton (only when <c>Identity:Backend</c> is <c>InMemory</c> — Rest has
/// no local provisioning step) — lives in <c>GeoAssets.Shared</c> rather than
/// <c>GeoAssets.Web</c> (despite the WASM-only registration) so routed pages in this project
/// (e.g. the identity admin pages, XD01-58) can call <see cref="EnsureProvisionedAsync"/>
/// directly; <c>GeoAssets.Shared</c> cannot reference <c>GeoAssets.Web</c>. Initialized in
/// Program.cs:
/// <code>
///   host.Services.GetRequiredService&lt;UserProvisioningService&gt;();
/// </code>
/// </summary>
public sealed class UserProvisioningService : IAsyncDisposable
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IServiceScopeFactory        _scopeFactory;
    private readonly TimeProvider                _timeProvider;

    public UserProvisioningService(
        AuthenticationStateProvider authStateProvider,
        IServiceScopeFactory        scopeFactory,
        TimeProvider                timeProvider)
    {
        _authStateProvider = authStateProvider;
        _scopeFactory      = scopeFactory;
        _timeProvider      = timeProvider;
        _authStateProvider.AuthenticationStateChanged += OnAuthStateChanged;
    }

    private async void OnAuthStateChanged(Task<AuthenticationState> task)
    {
        try
        {
            var state = await task;
            if (state.User.Identity?.IsAuthenticated != true) return;

            await ProvisionAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UserProvisioningService] Error during JIT provisioning: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the current user is provisioned, awaited synchronously. See the class doc
    /// comment for why callers with an already-confirmed-authenticated context should prefer
    /// this over relying on the reactive <c>AuthenticationStateChanged</c> path alone.
    /// </summary>
    public Task EnsureProvisionedAsync() => ProvisionAsync();

    private async Task ProvisionAsync()
    {
        // Use a fresh scope because this runs outside a normal Blazor request scope
        await using var scope = _scopeFactory.CreateAsyncScope();

        var accessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var current = await accessor.GetCurrentUserAsync();
        if (current is null || string.IsNullOrEmpty(current.ExternalObjectId)) return;

        var existing = await userRepo.GetByExternalObjectIdAsync(current.ExternalObjectId);
        if (existing is not null) return; // already provisioned

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var newUser = new AppUser
        {
            ExternalObjectId = current.ExternalObjectId,
            Email            = current.Email,
            DisplayName      = current.DisplayName,
            CreatedAt        = now,
            LastLoginAt      = now,
        };

        await userRepo.AddAsync(newUser);
        await userRepo.SaveChangesAsync();

        Console.WriteLine($"[UserProvisioningService] Provisioned user: {current.Email} ({current.ExternalObjectId})");
    }

    public ValueTask DisposeAsync()
    {
        _authStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        return ValueTask.CompletedTask;
    }
}
