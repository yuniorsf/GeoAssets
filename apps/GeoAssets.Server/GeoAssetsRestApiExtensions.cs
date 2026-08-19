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
/// builder.Services.AddSingleton&lt;IAssetProvider&gt;(sp =>
///     sp.GetRequiredService&lt;IPostgresProviderFactory&gt;()
///       .Create(connectionString));
/// app.MapGeoAssetsApi();
/// </code>
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
    public static IEndpointRouteBuilder MapGeoAssetsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets")
    {
        var opts = GeoJsonSerializer.GetOptions();

        // ── Features ─────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/features", (IAssetProvider provider) =>
            Results.Json(provider.GetAll(), opts))
            .RequireAuthorization("features:read");

        routes.MapGet($"{prefix}/features/bounds",
            async (double minLon, double minLat, double maxLon, double maxLat, IAssetProvider provider) =>
                Results.Json(await provider.GetInBoundsAsync(minLon, minLat, maxLon, maxLat), opts))
            .RequireAuthorization("features:read");

        routes.MapGet($"{prefix}/features/{{id}}", async (
            string id, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var f = provider.GetById(id);
            if (f is null) return Results.NotFound();

            var result = await authz.AuthorizeAsync(http.User, f, new OrgResourceRequirement("features:read"));
            if (!result.Succeeded)
                return Results.Json(new { reason = "Not authorized to read this feature." },
                    statusCode: StatusCodes.Status403Forbidden);

            return Results.Json(f, opts);
        })
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/features", async (HttpRequest req, IAssetProvider provider) =>
        {
            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts);
            if (feature is null) return Results.BadRequest("Invalid feature.");
            provider.Add(feature);
            return Results.Created($"{prefix}/features/{feature.Id}", null);
        })
            .RequireAuthorization("features:edit");

        routes.MapPut($"{prefix}/features/{{id}}", async (
            string id, HttpRequest req, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var existing = provider.GetById(id);
            if (existing is not null)
            {
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:edit"));
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to edit this feature." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts);
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
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:delete"));
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to delete this feature." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            provider.Delete(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");

        routes.MapPost($"{prefix}/features/bulk", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts) ?? [];
            provider.AddRange(features);
            return Results.NoContent();
        })
            .RequireAuthorization("features:edit");

        routes.MapPost($"{prefix}/features/load", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts) ?? [];
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

        // ── Asset types ───────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/asset-types", (IAssetProvider provider) =>
            Results.Json(provider.GetAssetTypes(), opts))
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/asset-types", async (HttpRequest req, IAssetProvider provider) =>
        {
            var assetType = await JsonSerializer.DeserializeAsync<AssetType>(req.Body, opts);
            if (assetType is null) return Results.BadRequest("Invalid asset type.");
            provider.AddAssetType(assetType);
            return Results.Created($"{prefix}/asset-types/{assetType.Id}", null);
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/asset-types/{{id}}", async (
            Guid id, IAssetProvider provider, IAuthorizationService authz, HttpContext http) =>
        {
            var existing = provider.GetAssetTypes().FirstOrDefault(t => t.Id == id);
            if (existing is not null)
            {
                var result = await authz.AuthorizeAsync(http.User, existing, new OrgResourceRequirement("features:delete"));
                if (!result.Succeeded)
                    return Results.Json(new { reason = "Not authorized to delete this asset type." },
                        statusCode: StatusCodes.Status403Forbidden);
            }

            provider.DeleteAssetType(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");

        // WFS 2.0 and WMS 1.1.1 handlers mounted under the same CORS-configured prefix so
        // Blazor WASM clients can reach them without separate CORS origin entries.
        // External OGC clients can also use the standalone /wfs and /wms routes.
        routes.MapWfsApi(route: $"{prefix}/wfs");
        routes.MapWmsApi(route: $"{prefix}/wms");

        return routes;
    }
}
