using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// No-op implementation of <see cref="IRoleAssignmentProvider"/>.
///
/// Registered by default when role sync isn't configured, so Phase 1's local Roles/Users admin
/// CRUD keeps working with zero dependency on this feature (XD01-59 Phase 2).
///
/// <code>
///   services.AddSingleton&lt;IRoleAssignmentProvider, NullRoleAssignmentProvider&gt;();
/// </code>
/// </summary>
public sealed class NullRoleAssignmentProvider : IRoleAssignmentProvider
{
    public Task RegisterRoleAsync(AppRole role, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}
