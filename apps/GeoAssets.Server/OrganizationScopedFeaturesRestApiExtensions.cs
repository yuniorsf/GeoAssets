using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Server;

/// <summary>
/// Organization-scoped feature reads for trusted service callers (XD01-386). A vertical host reads
/// core assets with its own service principal — a client-credentials token that carries no
/// organization — on behalf of one of its own requests' organizations; these routes take that
/// organization explicitly in the path and answer from <see cref="IOrganizationCatalogFactory"/>'s
/// catalog for it (its own features plus the shared ones), so the vertical never needs the core
/// asset database (Invariant 1, Core Database Encapsulation).
///
/// Every route requires the <see cref="GeoAuthorizationHandler.ServiceCallerAppRole"/> app role and
/// nothing else: only a trusted internal service principal may name an organization, and that
/// role already satisfies every permission policy (see <see cref="GeoAuthorizationHandler"/>) —
/// a user token, whatever its permissions, is refused (403). The caller's own token organization,
/// if any, plays no part.
///
/// Routes (under <c>/api/geoassets</c> by default, same compact JSON options as the other asset routes):
/// <list type="bullet">
///   <item><c>GET organizations/{organizationId}/features/{id}</c> — the feature as that
///   organization sees it; 404 when it is not visible to it.</item>
///   <item><c>GET organizations/{organizationId}/features/{id}/descendants?edgeKind=&amp;skip=&amp;take=</c>
///   — a <see cref="PagedResult{T}"/> page of every feature reachable downstream of <c>id</c>
///   (<c>IAssetProvider.GetDescendants(id, edgeKind)</c> on that organization's catalog), in
///   ordinal id order so paging is stable; <c>take</c> defaults to <see cref="DefaultDescendantsPageSize"/>
///   and is capped at <see cref="MaxDescendantsPageSize"/>; 400 for a negative <c>skip</c> or a
///   non-positive <c>take</c>; 404 when the root feature is not visible to the organization.</item>
/// </list>
/// </summary>
public static class OrganizationScopedFeaturesRestApiExtensions
{
    public const int DefaultDescendantsPageSize = 500;
    public const int MaxDescendantsPageSize = 5_000;

    public static IEndpointRouteBuilder MapOrganizationScopedFeaturesApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets")
    {
        var opts = GeoJsonSerializer.CompactOptions;
        var group = $"{prefix}/organizations/{{organizationId:guid}}/features/{{id}}";

        routes.MapGet(group, async (
            Guid organizationId, string id, IOrganizationCatalogFactory catalogs, CancellationToken ct) =>
        {
            var catalog = await catalogs.GetForOrganizationAsync(organizationId, ct).ConfigureAwait(false);
            var feature = catalog.GetById(id);
            return feature is null ? Results.NotFound() : Results.Json(feature, opts);
        })
            .RequireAuthorization(policy => policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole));

        routes.MapGet($"{group}/descendants", async (
            Guid organizationId,
            string id,
            string? edgeKind,
            int? skip,
            int? take,
            IOrganizationCatalogFactory catalogs,
            CancellationToken ct) =>
        {
            var offset = skip ?? 0;
            var pageSize = take ?? DefaultDescendantsPageSize;
            if (offset < 0 || pageSize < 1)
                return Results.BadRequest("skip must be >= 0 and take must be >= 1.");
            pageSize = Math.Min(pageSize, MaxDescendantsPageSize);

            var catalog = await catalogs.GetForOrganizationAsync(organizationId, ct).ConfigureAwait(false);
            if (catalog.GetById(id) is null) return Results.NotFound();

            var descendants = catalog.GetDescendants(id, edgeKind)
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToList();

            return Results.Json(new PagedResult<GeoFeature>
            {
                Items = [.. descendants.Skip(offset).Take(pageSize)],
                TotalCount = descendants.Count,
            }, opts);
        })
            .RequireAuthorization(policy => policy.RequireRole(GeoAuthorizationHandler.ServiceCallerAppRole));

        return routes;
    }
}
