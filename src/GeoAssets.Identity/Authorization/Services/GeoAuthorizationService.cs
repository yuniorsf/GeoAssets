using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Default implementation of <see cref="IGeoAuthorizationService"/>.
///
/// Flow per authorization check:
///   1. Resolve current user via <see cref="ICurrentUserAccessor.GetCurrentUserAsync"/>
///   2. Look up <see cref="AppUser"/> by AzureObjectId in the repository
///   3. If user not yet provisioned, returns an empty (no-permissions) context (safe default)
///   4. Load roles, claims, effective permissions from the DB / store
///   5. Evaluate the requested condition or policy
/// </summary>
public class GeoAuthorizationService(
    ICurrentUserAccessor   currentUserAccessor,
    IUserRepository        userRepository,
    IUserClaimRepository   claimRepository,
    IPolicyRepository      policyRepository) : IGeoAuthorizationService
{
    public async Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct);
        return ctx.HasRole(roleName);
    }

    public async Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct);
        return ctx.HasClaim(claimType, claimValue);
    }

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct);
        return ctx.HasPermission(permissionCode);
    }

    public async Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default)
    {
        var policy = await policyRepository.GetByNameAsync(policyName, ct)
            ?? throw new KeyNotFoundException($"Policy '{policyName}' not found.");
        return await EvaluatePolicyAsync(policy, ct);
    }

    public async Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct);
        return Evaluate(policy, ctx);
    }

    public virtual async Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
    {
        var current = await currentUserAccessor.GetCurrentUserAsync(ct)
            ?? throw new UnauthorizedAccessException("No authenticated user in the current context.");

        var user = await userRepository.GetByAzureObjectIdAsync(current.AzureObjectId, ct);

        // User not yet provisioned — return empty context (safe default).
        // Provisioning is handled by the host (e.g. UserProvisioningService in WASM).
        if (user is null)
        {
            return new AuthorizationContext
            {
                User        = new AppUser
                {
                    AzureObjectId = current.AzureObjectId,
                    Email         = current.Email,
                    DisplayName   = current.DisplayName
                },
                Roles       = [],
                Claims      = [],
                Permissions = []
            };
        }

        // Update last-login stamp (fire-and-forget; no await to keep the path fast)
        user.LastLoginAt = DateTime.UtcNow;
        await userRepository.UpdateAsync(user, ct);
        await userRepository.SaveChangesAsync(ct);

        var roles       = await userRepository.GetRolesAsync(user.Id, ct);
        var claims      = await claimRepository.GetByUserIdAsync(user.Id, ct);
        var permissions = await userRepository.GetEffectivePermissionsAsync(user.Id, ct);

        return new AuthorizationContext
        {
            User        = user,
            Roles       = roles.Select(r => r.Name).ToList(),
            Claims      = claims.ToList(),
            Permissions = permissions.Select(p => p.Code).ToList()
        };
    }

    // ── Policy evaluation engine ──────────────────────────────────────────────

    private static bool Evaluate(AppPolicy policy, AuthorizationContext ctx)
    {
        if (policy.Requirements.Count == 0)
            return true;

        var results = policy.Requirements.Select(req => EvaluateRequirement(req, ctx));

        return policy.Operator == PolicyOperator.All
            ? results.All(r => r)
            : results.Any(r => r);
    }

    private static bool EvaluateRequirement(PolicyRequirement req, AuthorizationContext ctx)
        => req.Type switch
        {
            RequirementType.Role       => ctx.HasRole(req.Value),
            RequirementType.Claim      => ctx.HasClaim(req.Value, req.ClaimValue),
            RequirementType.Permission => ctx.HasPermission(req.Value),
            _                          => false
        };
}
