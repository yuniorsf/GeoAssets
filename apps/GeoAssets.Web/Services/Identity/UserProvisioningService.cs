using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using Microsoft.AspNetCore.Components.Authorization;

namespace GeoAssets.Web.Services.Identity;

/// <summary>
/// Subscribes to <see cref="AuthenticationStateProvider.AuthenticationStateChanged"/>
/// and provisions a local <see cref="AppUser"/> the first time a user authenticates
/// (Just-In-Time provisioning).
///
/// No default role is granted (XD01-19) — role assignment is sourced from the external
/// provider's roles claim (<see cref="CurrentUser.ExternalRoles"/>), consumed by
/// <see cref="GeoAssets.Identity.Authorization.Services.GeoAuthorizationService"/>. A newly
/// provisioned user with no external role assignment gets a safe empty <c>Roles</c> list —
/// see <see cref="GeoAssets.Identity.Authorization.Services.AuthorizationContext"/>.
///
/// Registered as a singleton. Initialized in Program.cs:
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

            await ProvisionAsync(state.User);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UserProvisioningService] Error during JIT provisioning: {ex.Message}");
        }
    }

    private async Task ProvisionAsync(System.Security.Claims.ClaimsPrincipal principal)
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
