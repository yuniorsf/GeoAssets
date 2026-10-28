using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Authorization;

namespace  GeoAssets.Server;

/// <summary>
/// Maps GeoAssets REST API endpoints onto any <see cref="IEndpointRouteBuilder"/>.
/// The <see cref="IAssetProvider"/> registered in DI is used to serve all requests.
///
/// Typical host setup:
/// <code>
/// builder.Services.AddGeoAssetsPostgres();
/// await builder.Services.AddWarmedPostgresAssetProviderAsync(connectionString);
/// app.MapGeoAssetsApi();
/// </code>
/// (pre-warms the provider's cache before the host starts serving requests — XD01-173;
/// see <c>PostgresServiceExtensions.AddWarmedPostgresAssetProviderAsync</c>'s own doc comment)
///
/// CORS: add <c>builder.Services.AddCors()</c> and <c>app.UseCors()</c> on the host
/// when the Blazor WASM client is served from a different origin.
///
/// Every endpoint requires the matching <c>features:read</c>/<c>features:edit</c>/
/// <c>features:delete</c> <c>AppPermission</c> (XD01-15) via <c>AddGeoAuthorizationPolicyBridge()</c>
/// (XD01-13) — reads need <c>features:read</c>, creates/updates need <c>features:edit</c>,
/// deletes (including the bulk "clear all" endpoint) need <c>features:delete</c>.
///
/// The single-resource endpoints (<c>GET/PUT/DELETE /features/{id}</c>,
/// <c>DELETE /asset-types/{id}</c>) additionally run <see cref="OrgResourceAuthorizationHandler"/>
/// (XD01-21) once the resource is loaded — this subject-only gate answers "can this user
/// act on features at all"; the resource-based check that follows answers "can this user
/// act on *this* feature", scoped by owning organization / cross-org grant. Bulk/list
/// endpoints (no single loaded resource) and creates (no existing resource to check
/// ownership of) only run the subject-only gate.
/// </summary>
public static class GeoAssetsRestApiExtensions
{
    /// <summary>Upper bound on features returned by <c>GET /features/bounds</c> for a single
    /// request — a large viewport over a dense dataset otherwise has no ceiling on response
    /// size (observed: ~15.9MB for one bbox during XD01-159's audit). Same order of magnitude
    /// as <c>WfsProviderFactory.DefaultMaxFeatures</c>'s existing per-request cap convention.</summary>
    private const int MaxBoundsFeatureCount = 10_000;

    /// <param name="wmsRequireAuthentication">Forwarded to <see cref="WmsEndpointExtensions.MapWmsApi"/>
    /// for the <c>{prefix}/wms</c> alias mounted below — see that method's own doc comment (XD01-95).</param>
    public static IEndpointRouteBuilder MapGeoAssetsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets",
        bool   wmsRequireAuthentication = true)
    {
        var opts = GeoJsonSerializer.Options;

        MapFeaturesApi(routes, prefix, opts);
        MapAssetTypesApi(routes, prefix, opts);

        // WFS 2.0 and WMS 1.1.1 handlers mounted under the same CORS-configured prefix so
        // Blazor WASM clients can reach them without separate CORS origin entries.
        // External OGC clients can also use the standalone /wfs and /wms routes.
        routes.MapWfsApi(route: $"{prefix}/wfs");
        routes.MapWmsApi(route: $"{prefix}/wms", requireAuthentication: wmsRequireAuthentication);

        return routes;
    }

    private static void MapFeaturesApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapFeatureReads(routes, prefix, opts);
        MapFeatureTopologyReads(routes, prefix, opts);
        MapFeatureWrites(routes, prefix, opts);
    }

    private static void MapFeatureReads(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/features", (IAssetProvider provider) =>
            Results.Json(provider.GetAll(), opts))
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/features/query", async (HttpRequest req, IAssetProvider provider) =>
        {
            var query = await JsonSerializer.DeserializeAsync<AssetQuery>(req.Body, opts).ConfigureAwait(false);
            if (query is null) return Results.BadRequest("Invalid query.");
            var page = await provider.GetPageAsync(query).ConfigureAwait(false);
            return Results.Json(page, opts);
        })
            .RequireAuthorization("features:read");

        routes.MapGet($"{prefix}/features/bounds",
            async (double minLon, double minLat, double maxLon, double maxLat, IAssetProvider provider) =>
            {
                var features = await provider.GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
                if (features.Count > MaxBoundsFeatureCount)
                    features = [.. features.Take(MaxBoundsFeatureCount)];
                return Results.Json(features, opts);
            })
            .RequireAuthorization("features:read");

        routes.MapGet($"{prefix}/features/{{id}}", async (
            string id, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var f = provider.GetById(id);
            if (f is null) return Results.NotFound();

            var result = await authz.AuthorizeAsync(http.User, f, new OrgResourceRequirement("features:read")).ConfigureAwait(false);
            if (!result.Succeeded)
                return Results.Json(new { reason = "Not authorized to read this feature." },
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.Json(f, opts);
        })
            .RequireAuthorization("features:read");
    }

    private static void MapFeatureTopologyReads(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/features/{{id}}/topology/neighbors", async (
            string id,
            string? kind,
            string? targetAssetTypeId,
            bool? idsOnly,
            IAssetProvider provider,
            IAuthorizationService authz,
            HttpContext http) =>
        {
            var f = provider.GetById(id);
            if (f is null) return Results.NotFound();

            var result = await authz.AuthorizeAsync(http.User, f, new OrgResourceRequirement("features:read")).ConfigureAwait(false);
            if (!result.Succeeded)
                return Results.Json(new { reason = "Not authorized to read this feature." },
                    statusCode: StatusCodes.Status403Forbidden);

            var neighbors = provider.GetNeighbors(id, kind, targetAssetTypeId);
            if (idsOnly == true)
                return Results.Json(neighbors.Select(n => n.Id).ToList(), opts);

            return Results.Json(neighbors, opts);
        })
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/features/topology/neighbors-batch", async (
            HttpRequest req, IAssetProvider provider) =>
        {
            var batch = await JsonSerializer.DeserializeAsync<TopologyNeighborsBatchRequest>(req.Body, opts).ConfigureAwait(false);
            if (batch is null) return Results.BadRequest("Invalid batch request.");

            var result = new Dictionary<string, IReadOnlyList<string>>(batch.FeatureIds.Count, StringComparer.Ordinal);
            foreach (var id in batch.FeatureIds)
            {
                var neighbors = provider.GetNeighbors(id, batch.Kind, batch.TargetAssetTypeId);
                result[id] = [.. neighbors.Select(n => n.Id)];
            }

            return Results.Json(result, opts);
        })
            .RequireAuthorization("features:read");
    }

    private static void MapFeatureWrites(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapSingleFeatureWrites(routes, prefix, opts);
        MapBulkFeatureWrites(routes, prefix, opts);
    }

    private static void MapSingleFeatureWrites(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/features", async (HttpRequest req, IAssetProvider provider) =>
        {
            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts).ConfigureAwait(false);
            if (feature is null) return Results.BadRequest("Invalid feature.");
            provider.Add(feature);
            return Results.Created(new Uri($"{prefix}/features/{feature.Id}", UriKind.Relative), null);
        })
            .RequireAuthorization("features:edit");

        routes.MapPut($"{prefix}/features/{{id}}", async (
            string id, HttpRequest req, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var existing = provider.GetById(id);
            if (existing is not null)
            {
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:edit")).ConfigureAwait(false);
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to edit this feature." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts).ConfigureAwait(false);
            if (feature is null) return Results.BadRequest("Invalid feature.");
            feature.Id = id;
            provider.Update(feature);
            return Results.NoContent();
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/features/{{id}}", async (
            string id, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var existing = provider.GetById(id);
            if (existing is not null)
            {
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:delete")).ConfigureAwait(false);
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to delete this feature." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            provider.Delete(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");
    }

    private static void MapBulkFeatureWrites(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/features/bulk", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts).ConfigureAwait(false) ?? [];
            provider.AddRange(features);
            return Results.NoContent();
        })
            .RequireAuthorization("features:edit");

        routes.MapPost($"{prefix}/features/load", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts).ConfigureAwait(false) ?? [];
            provider.LoadAll(features);
            return Results.NoContent();
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/features", (IAssetProvider provider) =>
        {
            provider.Clear();
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");
    }

    private static void MapAssetTypesApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/asset-types", (IAssetProvider provider) =>
            Results.Json(provider.GetAssetTypes(), opts))
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/asset-types", async (HttpRequest req, IAssetProvider provider) =>
        {
            var assetType = await JsonSerializer.DeserializeAsync<AssetType>(req.Body, opts).ConfigureAwait(false);
            if (assetType is null) return Results.BadRequest("Invalid asset type.");
            provider.AddAssetType(assetType);
            return Results.Created(new Uri($"{prefix}/asset-types/{assetType.Id}", UriKind.Relative), null);
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/asset-types/{{id}}", async (
            Guid id, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var existing = provider.GetAssetTypes().FirstOrDefault(t => t.Id == id);
            if (existing is not null)
            {
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:delete")).ConfigureAwait(false);
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to delete this asset type." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            provider.DeleteAssetType(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");
    }
}
