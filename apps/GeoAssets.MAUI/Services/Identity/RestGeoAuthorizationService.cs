using System.Net.Http.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.MAUI.Services.Identity;

/// <summary>
/// HTTP-backed <see cref="IGeoAuthorizationService"/> — calls <c>GeoAssets.Server</c>'s
/// read-only <c>/api/identity/*</c> endpoints (XD01-18), same as
/// <c>GeoAssets.Web.Services.Identity.Rest.RestGeoAuthorizationService</c>. Duplicated here
/// (XD01-24) rather than shared, since that type lives inside the <c>GeoAssets.Web</c> app
/// project — not a project MAUI can reference — and every type it depends on
/// (<see cref="AuthorizationContextDto"/>, <see cref="PolicyDto"/>, the
/// <c>GeoAssets.Identity.Authorization</c> models) already lives in the shared
/// <c>GeoAssets.Identity</c> core project, so the two copies can't drift on wire shape, only
/// on this file's own ~100 lines. Same duplication spirit as
/// <c>MsalAuthorizationHandler</c>/<c>AuthorizationMessageHandler</c> (MAUI vs. Web auth
/// plumbing already isn't shared).
///
/// The current user's <see cref="AuthorizationContext"/> and the policy catalog are each
/// fetched once and cached for the lifetime of this instance (scoped — one per
/// <c>BlazorWebView</c> session). There is no cache-invalidation/refresh here — role or
/// permission changes on the server take effect on the next app restart.
/// </summary>
public sealed class RestGeoAuthorizationService(HttpClient http) : IGeoAuthorizationService
{
    private Task<AuthorizationContext>?          _context;
    private Task<IReadOnlyList<AppPolicy>>?      _policies;

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
        var policy = policies.FirstOrDefault(p => p.Name == policyName)
            ?? throw new KeyNotFoundException($"Policy '{policyName}' not found.");
        return await EvaluatePolicyAsync(policy, ct);
    }

    public async Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default)
    {
        var ctx = await GetAuthorizationContextAsync(ct);
        return Evaluate(policy, ctx);
    }

    public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
        => _context ??= FetchAuthorizationContextAsync(ct);

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
            Permissions = dto.Permissions
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
