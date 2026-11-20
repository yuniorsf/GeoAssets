using GeoAssets.Core.Interfaces;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Provider.PostgreSQL;

namespace GeoAssets.Server.Modules.PostgresSpatial;

/// <summary>
/// The PostgreSQL/PostGIS spatial plane as a module of this host (XD01-359, ADR-001 Phase 1): the <c>"postgres"</c>
/// provider plugin the kernel's catalog pool builds the asset store with (XD01-371), organization-scoped catalogs and
/// their middleware over the pool's default catalog, the PostGIS spatial query, the PostGIS render query behind
/// <c>IAssetRenderQuery</c> (XD01-372), the applied-batch store, and both WMS routes. Without it the kernel boots on whatever
/// <see cref="IAssetProvider"/> the host registers (invariant I5).
/// </summary>
public sealed class PostgresSpatialModule : IGeoAssetsModule, IModuleMiddleware
{
    public const string ModuleId = "postgres-spatial";

    /// <summary>Configuration key: whether the WMS routes require an authenticated caller (default <see langword="false"/>, XD01-95).</summary>
    public const string WmsRequireAuthenticationKey = "Wms:RequireAuthentication";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException($"The '{ModuleId}' module needs the 'GeoAssets' connection string.");

        // The "postgres" provider plugin (XD01-371): the kernel's catalog pool builds the asset store with it, migrated
        // and pre-warmed before the host serves — PostgresAssetProvider's ISyncProvider marker stays honest (XD01-173).
        services.AddGeoAssetsPostgres();

        // Organization-scoped asset reads (XD01-338), natively: one cache per organization over the pool's default catalog
        // (the host's singleton IAssetProvider, the shared catalog), registered as the host's IOrganizationCatalogFactory
        // (XD01-373); each request's IAssetProvider is the caller's organization's view.
        services.AddPostgresOrganizationAssetCatalogs(connectionString);

        // IAssetSpatialQuery (XD01-307): area/attribute selection pushed down to PostGIS, scoped to the request's tenant.
        services.AddPostgresAssetSpatialQuery(connectionString);

        // WMS (XD01-372): the PostGIS render pushdown behind IAssetRenderQuery, scoped to the request's tenant.
        services.AddPostgresAssetRenderQuery(connectionString);

        // IAssetMutationBatchApplier (XD01-337/XD01-374): the PostgreSQL pushdown the kernel's asset-mutation consumer
        // (XD01-383) applies batches with, reloading the organization catalogs after each batch.
        services.AddPostgresAssetMutationBatchApplier(connectionString);

        // IAppliedMutationBatchStore (XD01-229): a scoped per-request DbContext.
        services.AddPostgresAppliedMutationBatchStore(connectionString);
    }

    public void UseMiddleware(IApplicationBuilder app) => app.UseOrganizationAssetCatalogs();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // WMS is anonymous by default (Wms:RequireAuthentication; XD01-95's open decision) — see MapWmsApi's doc comment.
        var requireAuthentication = endpoints.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetValue(WmsRequireAuthenticationKey, defaultValue: false);

        // The WMS alias under the CORS-configured asset prefix (Blazor WASM clients), and the standalone OGC route.
        endpoints.MapWmsApi(route: "/api/geoassets/wms", requireAuthentication: requireAuthentication);
        endpoints.MapWmsApi(requireAuthentication: requireAuthentication);
    }
}
