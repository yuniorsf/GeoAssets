using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Web.Services.Identity.Rest;

/// <summary>
/// HTTP-backed <see cref="IGeoAuthorizationService"/> — calls <c>GeoAssets.Server</c>'s
/// read-only <c>/api/identity/*</c> endpoints (XD01-18) instead of evaluating against
/// client-only WASM state, so "what can I do" always matches the real server-side
/// identity backend (XD01-14).
///
/// The current user's <see cref="AuthorizationContext"/> and the policy catalog are each
/// fetched once and cached for the lifetime of this instance (scoped — in Blazor WASM,
/// <c>host.Services</c> is the root scope, so this effectively means once per session,
/// matching how <see cref="AuthorizationContext"/>'s own doc comment describes caching
/// "per request" for server hosts). There is no cache-invalidation/refresh here — role or
/// permission changes on the server take effect on the next full page load.
///
/// Unlike <see cref="GeoAuthorizationService"/> (the in-memory/EF-backed default
/// implementation), this type does not update <c>LastLoginAt</c> — that write happens as a
/// side effect of the server's own <c>GeoAuthorizationService</c> instance when it handles
/// the <c>/api/identity/me</c> request.
///
/// The cross-organization half of the context — <see cref="AuthorizationContext.OrganizationGrants"/>,
/// <see cref="AuthorizationContext.ScopedRoles"/>, <see cref="AuthorizationContext.ScopedPermissions"/> — comes from
/// <see cref="ICrossOrgEntitlementsService"/> (<c>GET /api/identity/me/cross-org-entitlements</c>, XD01-329), so the
/// client's <c>WorkflowPrincipal</c> carries what the server's does. Unlike <c>/me</c>, it is refreshable: after
/// <see cref="ICrossOrgEntitlementsService.RefreshAsync"/>, the next <see cref="GetAuthorizationContextAsync"/> returns a
/// context carrying the new entitlements.
/// </summary>
public sealed class RestGeoAuthorizationService(HttpClient http, ICrossOrgEntitlementsService entitlements) : IGeoAuthorizationService
{
    private Task<AuthorizationContext>?          _home;
    private (CrossOrgEntitlementsDto Entitlements, AuthorizationContext Context)? _composed;
    private Task<IReadOnlyList<AppPolicy>>?      _policies;

    /// <summary>Reads the entitlements over <paramref name="http"/> too (<see cref="RestCrossOrgEntitlementsService"/>).</summary>
    public RestGeoAuthorizationService(HttpClient http) : this(http, new RestCrossOrgEntitlementsService(http))
    {
    }

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
        var policies = await GetPoliciesAsync(ct);
        var policy = policies.FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Policy '{policyName}' not found.");
        return await EvaluatePolicyAsync(policy, ct);
    }

    public async Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var ctx = await GetAuthorizationContextAsync(ct);
        return Evaluate(policy, ctx);
    }

    public async Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
    {
        var home = await (_home ??= FetchAuthorizationContextAsync(ct));
        var current = await entitlements.GetAsync(ct);

        if (_composed is { } composed && ReferenceEquals(composed.Entitlements, current))
            return composed.Context;

        var context = WithEntitlements(home, current);
        _composed = (current, context);
        return context;
    }

    /// <summary>
    /// <paramref name="home"/> (<c>/me</c>) with the cross-organization entitlements. A caller with no organization holds
    /// no grant; its scoped roles still count, as on the server.
    /// </summary>
    private static AuthorizationContext WithEntitlements(AuthorizationContext home, CrossOrgEntitlementsDto entitlements) => new()
    {
        User = home.User,
        Roles = home.Roles,
        Claims = home.Claims,
        Permissions = home.Permissions,
        OrganizationGrants = home.User.OrganizationId is { } organizationId ? entitlements.ToGrants(organizationId) : [],
        ScopedRoles = entitlements.ToScopedRoles(),
        ScopedPermissions = entitlements.ToScopedPermissions(),
    };

    private async Task<AuthorizationContext> FetchAuthorizationContextAsync(CancellationToken ct)
    {
        var dto = await http.GetFromJsonAsync<AuthorizationContextDto>("me", ct)
            ?? throw new InvalidOperationException("GET /api/identity/me returned an empty response.");

        return new AuthorizationContext
        {
            User = new AppUser
            {
                Id             = dto.Id,
                Email          = dto.Email,
                DisplayName    = dto.DisplayName,
                OrganizationId = dto.OrganizationId,
                CreatedAt      = default,
            },
            Roles       = dto.Roles,
            Claims      = dto.Claims.Select(c => new UserClaim { Type = c.Type, Value = c.Value }).ToList(),
            Permissions = dto.Permissions,
        };
    }

    private Task<IReadOnlyList<AppPolicy>> GetPoliciesAsync(CancellationToken ct)
        => _policies ??= FetchPoliciesAsync(ct);

    private async Task<IReadOnlyList<AppPolicy>> FetchPoliciesAsync(CancellationToken ct)
    {
        var dtos = await http.GetFromJsonAsync<List<PolicyDto>>("policies", ct) ?? [];
        return dtos.Select(p => new AppPolicy
        {
            Id          = p.Id,
            Name        = p.Name,
            Description = p.Description,
            Operator    = p.Operator,
            Requirements = p.Requirements.Select(r => new PolicyRequirement
            {
                Type       = r.Type,
                Value      = r.Value,
                ClaimValue = r.ClaimValue
            }).ToList()
        }).ToList();
    }

    // ── Policy evaluation engine — mirrors GeoAuthorizationService's private logic ────
    // (duplicated rather than shared: that class is EF/server-oriented and not meant to
    // be reused as a client-side evaluator; the logic itself is ~10 lines and stable.)

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
