using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Services;

/// <summary>
/// Evaluates authorization rules for the currently authenticated user.
///
/// All checks are async because they may require database queries (roles, claims, policies).
/// Results can be cached at the request level via <see cref="GetAuthorizationContextAsync"/>.
/// </summary>
public interface IGeoAuthorizationService
{
    // ── Individual checks ─────────────────────────────────────────────────────

    /// <summary>Returns true if the current user has the named application role.</summary>
    Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>Returns true if the current user has the specified claim (optionally matching a value).</summary>
    Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default);

    /// <summary>Returns true if the current user has the specified permission (through any of their roles).</summary>
    Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default);

    // ── Policy evaluation ─────────────────────────────────────────────────────

    /// <summary>Evaluates a named policy against the current user's roles, claims, and permissions.</summary>
    Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default);

    /// <summary>Evaluates a policy object directly (useful for inline / dynamic policies).</summary>
    Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default);

    // ── Context ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the fully resolved authorization context for the current user
    /// (user record + role names + claims + permission codes).
    /// Cache this per-request to avoid redundant DB queries.
    /// </summary>
    Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default);
}
