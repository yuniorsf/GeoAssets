using System.Net.Http.Json;
using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// <see cref="IGeoAuthorizationService"/> for this host, which has no identity database (XD01-382): the caller's
/// authorization context is the kernel's answer to <c>GET /api/identity/me</c> for the caller's own token (forwarded by
/// <see cref="ForwardedBearerTokenHandler"/>), and named policies are the kernel's <c>GET /api/identity/policies</c> — so
/// <c>ServiceOrderRules</c> and the <c>RequireAuthorization("…")</c> permission policies decide here exactly what they decide
/// in the kernel. Scoped: each request reads each route at most once.
/// <para>
/// The cross-organization half of the context (XD01-409) — <see cref="AuthorizationContext.OrganizationGrants"/>,
/// <see cref="AuthorizationContext.ScopedRoles"/>, <see cref="AuthorizationContext.ScopedPermissions"/> — is the kernel's
/// <c>GET /api/identity/me/cross-org-entitlements</c> for the same token, as the Web client's
/// <c>RestGeoAuthorizationService</c> reads it, so <see cref="ServerWorkflowPrincipalFactory"/> builds here the principal
/// the kernel builds. It is read only for <see cref="GetAuthorizationContextAsync"/> (in parallel with <c>/me</c>): role,
/// claim, permission and policy checks are home-only and read <c>/me</c> alone. An entitlements answer that is not a
/// success, or that does not arrive (a network failure, a timeout, a body that is not the shape), is no entitlement — the
/// caller is then allowed less than the kernel would allow, never more, and a request that needs no cross-organization
/// access is served as before. A kernel 401 to the forwarded token still throws
/// <see cref="KernelRejectedCallerTokenException"/> (XD01-390).
/// </para>
/// </summary>
public sealed class KernelGeoAuthorizationService(HttpClient http) : IGeoAuthorizationService
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);
    private static readonly Uri MeRoute = new("me", UriKind.Relative);
    private static readonly Uri EntitlementsRoute = new("me/cross-org-entitlements", UriKind.Relative);

    private Task<AuthorizationContext>? _home;
    private Task<AuthorizationContext>? _context;
    private Task<IReadOnlyList<AppPolicy>>? _policies;

    public async Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) =>
        (await HomeAsync(ct).ConfigureAwait(false)).HasRole(roleName);

    public async Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) =>
        (await HomeAsync(ct).ConfigureAwait(false)).HasClaim(claimType, claimValue);

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
        (await HomeAsync(ct).ConfigureAwait(false)).HasPermission(permissionCode);

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

        var context = await HomeAsync(ct).ConfigureAwait(false);
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

    private Task<AuthorizationContext> HomeAsync(CancellationToken ct) => _home ??= FetchHomeAsync(ct);

    /// <summary>
    /// <c>/me</c> with the cross-organization entitlements. A caller with no organization holds no grant; its scoped roles
    /// still count, as in the kernel.
    /// </summary>
    private async Task<AuthorizationContext> FetchAuthorizationContextAsync(CancellationToken ct)
    {
        var entitlementsTask = FetchEntitlementsAsync(ct);
        var home = await HomeAsync(ct).ConfigureAwait(false);
        var entitlements = await entitlementsTask.ConfigureAwait(false);

        return new AuthorizationContext
        {
            User = home.User,
            Roles = home.Roles,
            Claims = home.Claims,
            Permissions = home.Permissions,
            OrganizationGrants = home.User.OrganizationId is { } organizationId ? entitlements.ToGrants(organizationId) : [],
            ScopedRoles = entitlements.ToScopedRoles(),
            ScopedPermissions = entitlements.ToScopedPermissions(),
        };
    }

    private async Task<CrossOrgEntitlementsDto> FetchEntitlementsAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(EntitlementsRoute, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return CrossOrgEntitlementsDto.None;

            return await response.Content.ReadFromJsonAsync<CrossOrgEntitlementsDto>(_opts, ct).ConfigureAwait(false)
                ?? CrossOrgEntitlementsDto.None;
        }
        catch (HttpRequestException)
        {
            return CrossOrgEntitlementsDto.None;
        }
        catch (JsonException)
        {
            return CrossOrgEntitlementsDto.None;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return CrossOrgEntitlementsDto.None;
        }
    }

    private async Task<AuthorizationContext> FetchHomeAsync(CancellationToken ct)
    {
        var dto = await http.GetFromJsonAsync<AuthorizationContextDto>(MeRoute, _opts, ct).ConfigureAwait(false)
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
