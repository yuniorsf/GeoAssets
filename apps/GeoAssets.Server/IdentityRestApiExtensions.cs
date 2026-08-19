using System.Text.Json;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Maps read-only identity/authorization REST endpoints onto any <see cref="IEndpointRouteBuilder"/>
/// (XD01-18) — lets <c>GeoAssets.Web</c> resolve "what can the current user do" against the real
/// server-side identity backend (<c>AddGeoIdentity()</c>, XD01-14) instead of client-only WASM state.
///
/// Every endpoint here requires an authenticated caller by default via the global
/// <c>AuthorizationOptions.FallbackPolicy</c> (XD01-12) — no explicit <c>.RequireAuthorization()</c>
/// needed. Read-only by design: creating/editing users, roles, permissions, or policies is a
/// separate, not-yet-scoped admin-API concern, not part of this endpoint set.
/// </summary>
public static class IdentityRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapIdentityApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/identity")
    {
        routes.MapGet($"{prefix}/me", async (IGeoAuthorizationService authService) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync();
            var dto = new AuthorizationContextDto(
                Id:             ctx.User.Id,
                Email:          ctx.User.Email,
                DisplayName:    ctx.User.DisplayName,
                OrganizationId: ctx.User.OrganizationId,
                Roles:          ctx.Roles,
                Claims:         ctx.Claims.Select(c => new ClaimDto(c.Type, c.Value)).ToList(),
                Permissions:    ctx.Permissions);

            return Results.Json(dto, _opts);
        });

        routes.MapGet($"{prefix}/policies", async (IPolicyRepository policyRepo) =>
        {
            var policies = await policyRepo.GetAllAsync();
            var dtos = policies.Select(p => new PolicyDto(
                Id:           p.Id,
                Name:         p.Name,
                Description:  p.Description,
                Operator:     p.Operator,
                Requirements: p.Requirements
                    .Select(r => new PolicyRequirementDto(r.Type, r.Value, r.ClaimValue))
                    .ToList()));

            return Results.Json(dtos, _opts);
        });

        return routes;
    }
}
