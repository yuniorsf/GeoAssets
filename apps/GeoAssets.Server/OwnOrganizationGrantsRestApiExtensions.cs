using System.Text.Json;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Maps <c>GET {prefix}/organization-grants/me</c> (XD01-332) — the active, unexpired
/// cross-organization grants held by the caller's OWN organization. Mapped by the host next
/// to <see cref="IdentityRestApiExtensions.MapIdentityApi"/>, under the same prefix.
///
/// Authenticated-only and self-service, with the same ownership philosophy as
/// <c>/invitations/me</c>: the organization is derived from the caller's own
/// <see cref="AuthorizationContext"/>, never from anything a client supplies, and no admin
/// permission is asked for — a grantee must be able to read what it was granted.
///
/// It exists so a client can build the <c>WorkflowPrincipal</c> the server builds
/// (<see cref="ServerWorkflowPrincipalFactory"/> reads the same repository member), and
/// <c>ServiceOrderRules</c>' organization gate then decides the same on both sides: the UI
/// neither offers an action the server will refuse nor hides one it would allow.
///
/// Always 200: a caller with no organization, or whose organization holds no grant, gets an
/// empty list. The wire shape, <see cref="OwnOrganizationGrantDto"/>, carries what rule
/// evaluation reads and nothing else (no grant id, no grantor).
/// </summary>
public static class OwnOrganizationGrantsRestApiExtensions
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapOwnOrganizationGrantsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet($"{prefix}/organization-grants/me", async (
            IOrganizationGrantRepository grantRepo, IGeoAuthorizationService authService, CancellationToken ct) =>
        {
            var ctx = await authService.GetAuthorizationContextAsync(ct).ConfigureAwait(false);
            if (ctx.User.OrganizationId is not { } organizationId)
                return Results.Json(Array.Empty<OwnOrganizationGrantDto>(), _opts);

            var grants = await grantRepo.GetActiveGrantsForGranteeAsync(organizationId, ct).ConfigureAwait(false);
            return Results.Json(grants.Select(OwnOrganizationGrantDto.FromModel), _opts);
        });

        return routes;
    }
}
