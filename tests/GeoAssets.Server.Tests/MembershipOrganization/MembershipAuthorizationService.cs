using System.Collections.Concurrent;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server.Tests.MembershipOrganization;

/// <summary>
/// Permits everything, and answers the caller's identity record from <see cref="Users"/> by object id — the organization
/// and active flag an administrator would have set — so a test controls what membership the kernel finds.
/// </summary>
internal sealed class MembershipAuthorizationService(ICurrentUserAccessor currentUser) : IGeoAuthorizationService
{
    public static ConcurrentDictionary<string, (Guid? OrganizationId, bool IsActive)> Users { get; } = new(StringComparer.Ordinal);

    public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => Task.FromResult(true);

    public async Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
    {
        var caller = await currentUser.GetCurrentUserAsync(ct)
            ?? throw new UnauthorizedAccessException("No authenticated user in the current context.");
        var (organizationId, isActive) = Users.TryGetValue(caller.ExternalObjectId, out var known) ? known : (null, true);

        return new AuthorizationContext
        {
            User = new AppUser
            {
                ExternalObjectId = caller.ExternalObjectId,
                Email = caller.ExternalObjectId + "@example.com",
                DisplayName = caller.ExternalObjectId,
                IsActive = isActive,
                OrganizationId = organizationId,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            Roles = caller.ExternalRoles,
            Claims = [],
            Permissions = [],
        };
    }
}
