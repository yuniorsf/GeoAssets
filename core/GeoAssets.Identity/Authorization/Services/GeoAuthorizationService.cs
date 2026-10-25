using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Default implementation of <see cref="IGeoAuthorizationService"/>.
///
/// Flow per authorization check:
///   1. Resolve current user via <see cref="ICurrentUserAccessor.GetCurrentUserAsync"/>
///   2. Look up <see cref="AppUser"/> by ExternalObjectId in the repository
///   3. If user not yet provisioned, JIT-provisions one now (XD01-88) — this is the only
///      provisioning path for the Rest backend, which has no client-side equivalent
///   4. Load claims from the DB / store; source roles from the external provider's roles
///      claim (<see cref="CurrentUser.ExternalRoles"/>, XD01-19) rather than the local
///      <c>UserRole</c> assignment table, then resolve each role name's permissions via
///      <see cref="IRoleRepository"/> — the permission taxonomy/policy engine stay local
///      (<see cref="AppRole"/>/<see cref="RolePermission"/>), only role *assignment* moves to
///      whichever provider is configured (see <see cref="IGeoAuthenticationProvider"/>, XD01-48).
///      The local <c>UserRole</c> table/repository methods are kept, unused by this flow, as
///      a rollback/dev-mode-fallback safety net rather than removed outright.
///   5. Evaluate the requested condition or policy
/// </summary>
public class GeoAuthorizationService(
    ICurrentUserAccessor   currentUserAccessor,
    IUserRepository        userRepository,
    IUserClaimRepository   claimRepository,
    IPolicyRepository      policyRepository,
    IRoleRepository        roleRepository,
    TimeProvider           timeProvider) : IGeoAuthorizationService
{
    private (string ExternalObjectId, IReadOnlyList<string> Roles, AuthorizationContext Context)? _cachedContext;
    private readonly Dictionary<string, AppPolicy> _policyCache = new(StringComparer.Ordinal);

    public async Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        return ctx.HasRole(roleName);
    }

    public async Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        return ctx.HasClaim(claimType, claimValue);
    }

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        return ctx.HasPermission(permissionCode);
    }

    public async Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default)
    {
        if (!_policyCache.TryGetValue(policyName, out var policy))
        {
            policy = await policyRepository.GetByNameAsync(policyName, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Policy '{policyName}' not found.");
            _policyCache[policyName] = policy;
        }

        return await EvaluatePolicyAsync(policy, ct).ConfigureAwait(false);
    }

    public async Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var ctx = await GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        return Evaluate(policy, ctx);
    }

    public virtual async Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
    {
        var current = await currentUserAccessor.GetCurrentUserAsync(ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No authenticated user in the current context.");

        if (_cachedContext is { } cached
            && string.Equals(cached.ExternalObjectId, current.ExternalObjectId, StringComparison.Ordinal)
            && cached.Roles.SequenceEqual(current.ExternalRoles, StringComparer.Ordinal))
        {
            return cached.Context;
        }

        var user = await userRepository.GetByExternalObjectIdAsync(current.ExternalObjectId, ct).ConfigureAwait(false);

        if (user is null)
        {
            // JIT-provision (XD01-88): the WASM client-side path (UserProvisioningService)
            // never runs against this Server host, so this is the only place a Rest-backend
            // caller's AppUser row gets created. Mirrors UserProvisioningService.ProvisionAsync
            // field-for-field — OrganizationId stays null; org/role assignment happens
            // post-first-login via the existing admin UI (XD01-63), never resolved
            // automatically here (see XD01-49's resolution for why that's permanent, not a gap).
            var now = timeProvider.GetUtcNow().UtcDateTime;
            user = new AppUser
            {
                ExternalObjectId = current.ExternalObjectId,
                Email            = current.Email,
                DisplayName      = current.DisplayName,
                CreatedAt        = now,
                LastLoginAt      = now,
            };

            await userRepository.AddAsync(user, ct).ConfigureAwait(false);
            await userRepository.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var claims = await claimRepository.GetByUserIdAsync(user.Id, ct).ConfigureAwait(false);

        // XD01-19: roles are sourced from the external provider's roles claim, not the local
        // UserRole assignment table. The permission each role name grants still resolves
        // against the local AppRole/RolePermission tables — a role name with no matching
        // local AppRole (not yet created by an admin) simply contributes no permissions,
        // rather than failing the whole lookup. Runs for a just-provisioned user too (not just
        // already-provisioned ones), so a brand-new caller's very first request already reflects
        // their real permissions instead of reporting an empty set until a second round-trip.
        var roles = current.ExternalRoles;
        var permissions = await roleRepository.GetPermissionsByRoleNamesAsync(roles, ct).ConfigureAwait(false);

        var context = new AuthorizationContext
        {
            User        = user,
            Roles       = roles,
            Claims      = claims.ToList(),
            Permissions = permissions.Select(p => p.Code).Distinct(StringComparer.Ordinal).ToList()
        };

        _cachedContext = (current.ExternalObjectId, roles, context);
        return context;
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
