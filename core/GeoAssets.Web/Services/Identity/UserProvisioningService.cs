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
/// New users are assigned the <see cref="IdentitySeeder.ReadOnlyRoleId"/> role by default.
/// An administrator can then elevate their role via the user management UI.
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

    public UserProvisioningService(
        AuthenticationStateProvider authStateProvider,
        IServiceScopeFactory        scopeFactory)
    {
        _authStateProvider = authStateProvider;
        _scopeFactory      = scopeFactory;
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
        if (current is null || string.IsNullOrEmpty(current.AzureObjectId)) return;

        var existing = await userRepo.GetByAzureObjectIdAsync(current.AzureObjectId);
        if (existing is not null) return; // already provisioned

        var newUser = new AppUser
        {
            AzureObjectId = current.AzureObjectId,
            Email         = current.Email,
            DisplayName   = current.DisplayName,
            CreatedAt     = DateTime.UtcNow,
            LastLoginAt   = DateTime.UtcNow,
        };

        await userRepo.AddAsync(newUser);

        // Assign default ReadOnly role
        await userRepo.AssignRoleAsync(newUser.Id, IdentitySeeder.ReadOnlyRoleId, assignedBy: "system");
        await userRepo.SaveChangesAsync();

        Console.WriteLine($"[UserProvisioningService] Provisioned user: {current.Email} ({current.AzureObjectId})");
    }

    public ValueTask DisposeAsync()
    {
        _authStateProvider.AuthenticationStateChanged -= OnAuthStateChanged;
        return ValueTask.CompletedTask;
    }
}
