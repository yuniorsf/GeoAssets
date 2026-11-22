using System.Text.Json;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Maps <c>GET {prefix}/me/cross-org-entitlements</c> (XD01-329) — the caller's own entitlements on other organizations
/// (<see cref="CrossOrgEntitlementsDto"/>): the active, unexpired grants held by the caller's organization, and the roles
/// and permission codes the caller holds on other organizations, each organization with its name. Mapped by the host next
/// to <see cref="OwnOrganizationGrantsRestApiExtensions.MapOwnOrganizationGrantsApi"/>, under the same prefix.
///
/// Authenticated-only (the host's fallback policy, like <c>/me</c>) and self-service: everything is derived from the
/// caller's own <see cref="AuthorizationContext"/>, never from anything a client supplies, so it never returns another
/// user's data. It reads the members <see cref="ServerWorkflowPrincipalFactory"/> reads — the grant repository and the
/// context's scoped roles — so a client builds the same <c>WorkflowPrincipal</c> from it and <c>ServiceOrderRules</c>
/// decides the same on both sides. Always 200: a caller with none gets empty lists.
/// </summary>
public static class CrossOrgEntitlementsRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapCrossOrgEntitlementsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet($"{prefix}/me/cross-org-entitlements", async (
            IGeoAuthorizationService authService, IOrganizationGrantRepository grantRepo, IOrganizationRepository orgRepo,
            CancellationToken ct) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            IReadOnlyList<OrganizationGrant> grants = ctx.User.OrganizationId is { } organizationId
                ? await grantRepo.GetActiveGrantsForGranteeAsync(organizationId, ct).ConfigureAwait(false)
                : [];

            var names = new Dictionary<Guid, string>();
            foreach (var id in CrossOrgEntitlementsDto.OrganizationsToName(ctx, grants))
            {
                if (await orgRepo.GetByIdAsync(id, ct).ConfigureAwait(false) is { } organization)
                    names[id] = organization.Name;
            }

            return Results.Json(CrossOrgEntitlementsDto.Create(ctx, grants, names), _opts);
        });

        return routes;
    }
}
