using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// <see cref="IGeoAuthorizationService"/> for this host, which has no identity database (XD01-382): the caller's
/// authorization context is the kernel's answer to <c>GET /api/identity/me</c> for the caller's own token (forwarded by
/// <see cref="ForwardedBearerTokenHandler"/>), and named policies are the kernel's <c>GET /api/identity/policies</c> — so
/// <c>ServiceOrderRules</c> and the <c>RequireAuthorization("…")</c> permission policies decide here exactly what they decide
/// in the kernel. Scoped: each request reads its context at most once.
/// </summary>
internal sealed class KernelGeoAuthorizationService(HttpClient http) : IGeoAuthorizationService
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private Task<AuthorizationContext>? _context;
    private Task<IReadOnlyList<AppPolicy>>? _policies;

    public async Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) =>
        (await GetAuthorizationContextAsync(ct).ConfigureAwait(false)).HasRole(roleName);

    public async Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) =>
        (await GetAuthorizationContextAsync(ct).ConfigureAwait(false)).HasClaim(claimType, claimValue);

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
        (await GetAuthorizationContextAsync(ct).ConfigureAwait(false)).HasPermission(permissionCode);

    public async Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default)
    {
        var policies = await (_policies ??= FetchPoliciesAsync(ct)).ConfigureAwait(false);
        var policy = policies.FirstOrDefault(p => string.Equals(p.Name, policyName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Policy '{policyName}' not found.");
        return await EvaluatePolicyAsync(policy, ct).ConfigureAwait(false);
    }

    public async Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var context = await GetAuthorizationContextAsync(ct).ConfigureAwait(false);
        if (policy.Requirements.Count == 0) return true;

        var results = policy.Requirements.Select(requirement => requirement.Type switch
        {
            RequirementType.Role => context.HasRole(requirement.Value),
            RequirementType.Claim => context.HasClaim(requirement.Value, requirement.ClaimValue),
            RequirementType.Permission => context.HasPermission(requirement.Value),
            _ => false,
        });

        return policy.Operator == PolicyOperator.All ? results.All(r => r) : results.Any(r => r);
    }

    public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
        _context ??= FetchAuthorizationContextAsync(ct);

    private async Task<AuthorizationContext> FetchAuthorizationContextAsync(CancellationToken ct)
    {
        var dto = await http.GetFromJsonAsync<AuthorizationContextDto>(new Uri("me", UriKind.Relative), _opts, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The kernel's GET /api/identity/me returned an empty response.");

        return new AuthorizationContext
        {
            User = new AppUser
            {
                Id = dto.Id,
                Email = dto.Email,
                DisplayName = dto.DisplayName,
                OrganizationId = dto.OrganizationId,
                CreatedAt = default,
            },
            Roles = dto.Roles,
            Claims = [.. dto.Claims.Select(c => new UserClaim { Type = c.Type, Value = c.Value })],
            Permissions = dto.Permissions,
        };
    }

    private async Task<IReadOnlyList<AppPolicy>> FetchPoliciesAsync(CancellationToken ct)
    {
        var dtos = await http.GetFromJsonAsync<List<PolicyDto>>(new Uri("policies", UriKind.Relative), _opts, ct).ConfigureAwait(false) ?? [];
        return [.. dtos.Select(p => new AppPolicy
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Operator = p.Operator,
            Requirements = [.. p.Requirements.Select(r => new PolicyRequirement { Type = r.Type, Value = r.Value, ClaimValue = r.ClaimValue })],
        })];
    }
}
