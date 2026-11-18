using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>Grants every permission and policy, so the kernel-only host's authorization checks never get in the way.</summary>
internal sealed class KernelOnlyPermittingAuthorizationService : IGeoAuthorizationService
{
    public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => Task.FromResult(true);

    public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
        Task.FromResult(new AuthorizationContext
        {
            User = new AppUser
            {
                Id = Guid.NewGuid(),
                Email = "kernel-only@example.com",
                DisplayName = "Kernel Only",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            Roles = [],
            Claims = [],
            Permissions = [],
        });
}
