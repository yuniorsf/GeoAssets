using System.Text.Json;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// The kernel's generic organization-grant API (XD01-385, ADR-001 Phase 7) — how a vertical host provisions the
/// cross-organization grants its own reference data implies, now that no vertical runs in the kernel:
/// <list type="bullet">
///   <item><c>GET {prefix}/organization-grants?granteeOrganizationId=&amp;resourceOrganizationId=&amp;page=&amp;pageSize=</c> —
///   one organization pair's active, unexpired grants, paged (<c>page</c> from 1, <c>pageSize</c> 1–500, default 100);</item>
///   <item><c>PUT {prefix}/organization-grants</c> — the idempotent upsert of <see cref="OrganizationGrantUpsertRequest"/>;</item>
///   <item><c>POST {prefix}/organization-grants/revoke</c> — deactivates the active grants with an
///   <see cref="OrganizationGrantKey"/>; idempotent.</item>
/// </list>
/// The organizations are always named in the query or the body, never taken from a header or the caller's own claims.
/// Only a trusted service principal (the <see cref="GeoAuthorizationHandler.ServiceCallerAppRole"/> app role) may call
/// them — the same bar as the vertical permission registration: the vertical decides who may ask it for a grant, the
/// kernel only trusts the vertical. An invalid request answers 400 <c>{ error }</c>. The store keeps one active grant per
/// key (XD01-394); an upsert that still loses to concurrent writes of its key after its one retry answers 409
/// <c>application/problem+json</c> with code <see cref="ConflictProblemCode"/> — safe to repeat.
/// </summary>
public static class OrganizationGrantsRestApiExtensions
{
    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 500;

    /// <summary>The problem code of the 409 an upsert answers when concurrent writes of its key outran its retry (XD01-394).</summary>
    public const string ConflictProblemCode = "organization-grant.conflict";

    private static readonly JsonSerializerOptions _opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapOrganizationGrantsApi(this IEndpointRouteBuilder routes, string prefix = "/api/identity")
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet($"{prefix}/organization-grants", async (
            Guid granteeOrganizationId,
            Guid resourceOrganizationId,
            int? page,
            int? pageSize,
            OrganizationGrantService service,
            CancellationToken ct) =>
        {
            var pageNumber = page ?? 1;
            var size = pageSize ?? DefaultPageSize;
            if (pageNumber < 1) return Results.BadRequest(new { error = "page must be 1 or more." });
            if (size < 1 || size > MaxPageSize) return Results.BadRequest(new { error = $"pageSize must be between 1 and {MaxPageSize}." });

            var result = await service
                .ListActiveAsync(granteeOrganizationId, resourceOrganizationId, pageNumber, size, ct)
                .ConfigureAwait(false);
            return Results.Json(result, _opts);
        }).RequireAuthorization(ServiceCallerOnly);

        routes.MapPut($"{prefix}/organization-grants", async (
            OrganizationGrantUpsertRequest? request, OrganizationGrantService service, TimeProvider timeProvider, CancellationToken ct) =>
        {
            if (request is null) return Results.BadRequest(new { error = "The grant is missing." });
            if (request.FindProblem() is { } problem) return Results.BadRequest(new { error = problem });

            // An already-expired grant would be invisible to the active-grant lookup, so every repeat would create another.
            if (request.ExpiresAt is { } expiresAt && expiresAt.ToUniversalTime() <= timeProvider.GetUtcNow().UtcDateTime)
                return Results.BadRequest(new { error = "expiresAt must be in the future." });

            try
            {
                var result = await service.UpsertAsync(request, ct).ConfigureAwait(false);
                return Results.Json(result, _opts);
            }
            catch (OrganizationGrantKeyConflictException)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "The organization grant changed concurrently.",
                    detail: "Another write of an active grant with this key raced this upsert; repeat the request.",
                    extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = ConflictProblemCode });
            }
        }).RequireAuthorization(ServiceCallerOnly);

        routes.MapPost($"{prefix}/organization-grants/revoke", async (
            OrganizationGrantKey? key, OrganizationGrantService service, CancellationToken ct) =>
        {
            if (key is null) return Results.BadRequest(new { error = "The grant key is missing." });
            if (key.FindProblem() is { } problem) return Results.BadRequest(new { error = problem });

            var result = await service.RevokeAsync(key, ct).ConfigureAwait(false);
            return Results.Json(result, _opts);
        }).RequireAuthorization(ServiceCallerOnly);

        return routes;
    }

    private static void ServiceCallerOnly(AuthorizationPolicyBuilder policy) =>
        policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole);
}
