using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Isp.NeutralOperator.GisExport;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Authorization;

namespace GeoAssets.Server;

/// <summary>
/// Maps GeoAssets REST API endpoints onto any <see cref="IEndpointRouteBuilder"/>.
/// The <see cref="IAssetProvider"/> registered in DI is used to serve all requests.
///
/// Typical host setup:
/// <code>
/// builder.Services.AddServerCatalogPool(builder.Configuration); // GeoAssets:Providers (XD01-371)
/// var app = builder.Build();
/// await app.Services.InitializeCatalogPoolAsync();
/// app.MapGeoAssetsApi();
/// </code>
/// (the catalogs are built and pre-warmed before the host starts serving requests — XD01-173;
/// see <c>CatalogPool</c>'s own doc comment)
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
///
/// Organization-scoped writes (XD01-340): with the host's organization catalogs
/// (<c>OrganizationAssetCatalog</c>), creates, bulk adds and loads are stamped with the caller's
/// organization, whatever the body states. A caller with an organization that <c>PUT</c>s an id it
/// cannot see gets 404, and any write the provider refuses for naming another organization's
/// feature (<see cref="ForeignGeoFeatureWriteException"/>) is answered 404 with nothing written —
/// another organization's ids are never disclosed.
/// </summary>
public static class GeoAssetsRestApiExtensions
{
    /// <summary>Upper bound on features returned by <c>GET /features/bounds</c> for a single
    /// request — a large viewport over a dense dataset otherwise has no ceiling on response
    /// size (observed: ~15.9MB for one bbox during XD01-159's audit). Same order of magnitude
    /// as <c>WfsProviderFactory.DefaultMaxFeatures</c>'s existing per-request cap convention.</summary>
    private const int MaxBoundsFeatureCount = 10_000;

    /// <summary>
    /// The kernel's asset endpoints under <paramref name="prefix"/>, with the WFS alias. The WMS alias
    /// (<c>{prefix}/wms</c>) renders straight from PostGIS, so the PostgreSQL spatial module maps it (XD01-359).
    /// </summary>
    public static IEndpointRouteBuilder MapGeoAssetsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets")
    {
        var opts = GeoJsonSerializer.Options;

        MapFeaturesApi(routes, prefix, opts);
        MapAssetTypesApi(routes, prefix, opts);
        MapLayersApi(routes, prefix, opts);
        MapMutationBatchesApi(routes, prefix, opts);
        MapGisExportApi(routes, prefix);

        // WFS 2.0 handler mounted under the same CORS-configured prefix so Blazor WASM clients can
        // reach it without separate CORS origin entries. External OGC clients can also use the
        // standalone /wfs route.
        routes.MapWfsApi(route: $"{prefix}/wfs");

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

            // An organization's caller updates only a feature it can see (XD01-340): another
            // organization's id is answered as unknown, so it is neither overwritten nor disclosed.
            if (existing is null && CallersOrganization(http) != Guid.Empty)
                return Results.NotFound();

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
            try
            {
                provider.Update(feature);
            }
            catch (ForeignGeoFeatureWriteException)
            {
                return Results.NotFound();
            }

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
            try
            {
                provider.AddRange(features);
            }
            catch (ForeignGeoFeatureWriteException)
            {
                return Results.NotFound();
            }

            return Results.NoContent();
        })
            .RequireAuthorization("features:edit");

        routes.MapPost($"{prefix}/features/load", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts).ConfigureAwait(false) ?? [];
            try
            {
                provider.LoadAll(features);
            }
            catch (ForeignGeoFeatureWriteException)
            {
                return Results.NotFound();
            }

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

    /// <summary>The request's organization (<see cref="ITenantContext"/>); <see cref="Guid.Empty"/> when it has none or none is registered.</summary>
    private static Guid CallersOrganization(HttpContext http) =>
        http.RequestServices.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty;

    /// <summary>
    /// Applies an <see cref="AssetMutationBatchRequestedEvent"/> atomically against
    /// <see cref="IAssetProvider"/> (XD01-229) — the apply side of the outbox-dispatched batch
    /// <c>InspectionCommitService</c> (XD01-228) stages from <c>GeoAssets.FieldService.Server</c>.
    /// Idempotent by <see cref="AssetMutationBatchRequestedEvent.BatchId"/>: a redelivered batch
    /// returns the original <see cref="AssetBatchAppliedEvent"/> without re-applying anything —
    /// see <see cref="AssetMutationBatchApplier"/>'s own doc comment for why this is
    /// <c>BatchId</c>-keyed rather than per-item optimistic concurrency.
    /// </summary>
    private static void MapMutationBatchesApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapPost($"{prefix}/mutation-batches", async (
            HttpRequest req, IAssetProvider provider, IAppliedMutationBatchStore store, TimeProvider timeProvider, CancellationToken ct) =>
        {
            var batch = await JsonSerializer.DeserializeAsync<AssetMutationBatchRequestedEvent>(req.Body, opts, ct).ConfigureAwait(false);
            if (batch is null) return Results.BadRequest("Invalid mutation batch.");

            var applied = await new IdempotentMutationBatchApplier(provider, store, timeProvider)
                .ApplyAsync(batch, ct).ConfigureAwait(false);

            return Results.Json(applied, opts);
        })
            .RequireAuthorization("features:edit");
    }

    /// <summary>
    /// Generic concessionaire GIS export (XD01-271) — projects every feature of the given
    /// <paramref name="assetTypeId"/> (the caller's own configured asset-type catalog has no
    /// fixed "pole" id, so the caller names which of their asset types that is) through
    /// <see cref="GisExportProjector"/> into the versioned contract documented on
    /// <see cref="GisExportSchema"/>, and returns it as plain RFC 7946 GeoJSON
    /// (<c>application/geo+json</c>) — consumable by GE Smallworld/Esri ArcGIS Enterprise or any
    /// other generic GIS import with zero GeoAssets-specific knowledge. No vendor-specific
    /// adapter is implemented here, by design (out of scope for this epic). Compliance-status
    /// fields report <see cref="GisExportSchema.ComplianceUnknown"/> for every feature until a
    /// persisted <see cref="PoleAttachmentSlotMatrix"/> store/DI registration exists — none does
    /// as of this ticket (see <see cref="NullPoleAttachmentSlotMatrixProvider"/>).
    /// </summary>
    private static void MapGisExportApi(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapGet($"{prefix}/export/gis/v1", (string assetTypeId, IAssetProvider provider) =>
        {
            var features = provider.GetByAssetType(assetTypeId);
            var collection = GisExportProjector.Project(features);
            return Results.Text(GisExportProjector.ToGeoJson(collection), "application/geo+json");
        })
            .RequireAuthorization("features:read");
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
            if (AssetTypeZoomRange.FindDefect(assetType.MinZoom, assetType.MaxZoom) is { } zoomDefect)
                return Results.BadRequest($"Invalid asset type zoom range: {zoomDefect}.");
            if (AssetTypeRoles.FindDefect(assetType.Roles) is { } roleDefect)
                return Results.BadRequest($"Invalid asset type roles: {roleDefect}.");
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

    /// <summary>
    /// Layer / layer-rule routes (XD01-153), mirroring <see cref="MapAssetTypesApi"/>. Unlike
    /// <c>DELETE /asset-types/{id}</c> these run the subject-only policy gate alone:
    /// <see cref="Layer"/> and <see cref="LayerRule"/> carry no owning organization, so there is
    /// no resource for <see cref="OrgResourceAuthorizationHandler"/> to evaluate.
    /// </summary>
    private static void MapLayersApi(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        MapLayerRoutes(routes, prefix, opts);
        MapLayerRuleRoutes(routes, prefix, opts);
    }

    private static void MapLayerRoutes(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        routes.MapGet($"{prefix}/layers", (IAssetProvider provider) =>
            Results.Json(provider.GetLayers(), opts))
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/layers", async (HttpRequest req, IAssetProvider provider) =>
        {
            var layer = await JsonSerializer.DeserializeAsync<Layer>(req.Body, opts).ConfigureAwait(false);
            if (layer is null) return Results.BadRequest("Invalid layer.");
            provider.AddLayer(layer);
            return Results.Created(new Uri($"{prefix}/layers/{layer.Id}", UriKind.Relative), null);
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/layers/{{id}}", (Guid id, IAssetProvider provider) =>
        {
            provider.DeleteLayer(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");
    }

    private static void MapLayerRuleRoutes(IEndpointRouteBuilder routes, string prefix, JsonSerializerOptions opts)
    {
        // IAssetProvider.GetLayerRules is per asset type, but a REST client needs every rule at
        // startup — so the unfiltered form fans out over the provider's asset types.
        routes.MapGet($"{prefix}/layer-rules", (Guid? assetTypeId, IAssetProvider provider) =>
        {
            IReadOnlyList<LayerRule> rules = assetTypeId is { } id
                ? provider.GetLayerRules(id)
                : [.. provider.GetAssetTypes().SelectMany(t => provider.GetLayerRules(t.Id))];
            return Results.Json(rules, opts);
        })
            .RequireAuthorization("features:read");

        routes.MapPost($"{prefix}/layer-rules", async (HttpRequest req, IAssetProvider provider) =>
        {
            var layerRule = await JsonSerializer.DeserializeAsync<LayerRule>(req.Body, opts).ConfigureAwait(false);
            if (layerRule is null) return Results.BadRequest("Invalid layer rule.");
            provider.AddLayerRule(layerRule);
            return Results.Created(new Uri($"{prefix}/layer-rules/{layerRule.Id}", UriKind.Relative), null);
        })
            .RequireAuthorization("features:edit");

        routes.MapDelete($"{prefix}/layer-rules/{{id}}", (Guid id, IAssetProvider provider) =>
        {
            provider.DeleteLayerRule(id);
            return Results.NoContent();
        })
            .RequireAuthorization("features:delete");
    }
}
