using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Server.Tiles;

namespace GeoAssets.Server.Modules.PostgresSpatial;

/// <summary>
/// The PostgreSQL/PostGIS spatial plane as a module of this host (XD01-359, ADR-001 Phase 1): the <c>"postgres"</c>
/// provider plugin the kernel's catalog pool builds the asset store with (XD01-371), organization-scoped catalogs and
/// their middleware over the pool's default catalog, the PostGIS spatial query, the PostGIS render query behind
/// <c>IAssetRenderQuery</c> (XD01-372), the PostGIS vector tile source behind <c>IVectorTileSource</c> (XD01-432), the
/// applied-batch store, the bulk feature importer behind <c>IFeatureBulkImporter</c>, both WMS routes and the tile endpoints
/// with their cache and dirty-tile invalidation (XD01-434). Without it the kernel boots on whatever <see cref="IAssetProvider"/>
/// the host registers (invariant I5).
/// </summary>
public sealed class PostgresSpatialModule : IGeoAssetsModule, IModuleMiddleware
{
    public const string ModuleId = "postgres-spatial";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException($"The '{ModuleId}' module needs the 'GeoAssets' connection string.");

        // The "postgres" provider plugin (XD01-371): the kernel's catalog pool builds the asset store with it, migrated
        // and pre-warmed before the host serves — PostgresAssetProvider's ISyncProvider marker stays honest (XD01-173).
        // Composed through its plugin module (XD01-428), so a drop-in plugin claiming the same id fails the startup.
        services.AddDiscoveredProviderPlugins(
            configuration, ProviderHostTargets.Server, new StaticAssemblyPluginSource().Add<PostgresProviderPluginModule>());

        // Organization-scoped asset reads (XD01-338), natively: one cache per organization over the pool's default catalog
        // (the host's singleton IAssetProvider, the shared catalog), registered as the host's IOrganizationCatalogFactory
        // (XD01-373); each request's IAssetProvider is the caller's organization's view.
        services.AddPostgresOrganizationAssetCatalogs(connectionString);
        // Only an organization with few enough features is cached whole; a larger one is served from PostGIS (XD01-449).
        services.AddOptions<OrganizationCatalogOptions>().BindConfiguration(OrganizationCatalogOptions.SectionName);

        // IAssetSpatialQuery (XD01-307): area/attribute selection pushed down to PostGIS, scoped to the request's tenant.
        services.AddPostgresAssetSpatialQuery(connectionString);

        // WMS (XD01-372): the PostGIS render pushdown behind IAssetRenderQuery, scoped to the request's tenant.
        services.AddPostgresAssetRenderQuery(connectionString);

        // Vector tiles (XD01-432): the PostGIS ST_AsMVT source behind IVectorTileSource, scoped to the request's tenant.
        services.AddPostgresVectorTileSource(connectionString);

        // The tile endpoints' state (XD01-434): GeoAssets:Tiles settings and LOD policy, the server tile cache, the layer
        // versions, and the ITileInvalidation the PostGIS writers below report their committed writes' dirty tiles to.
        services.AddTileServing(configuration);

        // The LOD calibration's density reads (XD01-439): the PostGIS ITileDensitySampler, scoped to the request's tenant.
        services.AddPostgresTileDensitySampler(connectionString);

        // Reference features (XD01-330): another organization's features, geometry and label only, read under that
        // organization's tenant for the one query.
        services.AddPostgresReferenceFeatureQuery(connectionString);

        // IAssetMutationBatchApplier (XD01-337/XD01-374): the PostgreSQL pushdown the kernel's asset-mutation consumer
        // (XD01-383) applies batches with, reloading the organization catalogs after each batch.
        services.AddPostgresAssetMutationBatchApplier(connectionString);

        // IAppliedMutationBatchStore (XD01-229): a scoped per-request DbContext.
        services.AddPostgresAppliedMutationBatchStore(connectionString);

        // IFeatureBulkImporter: chunked bulk feature imports (binary COPY + upsert), reloading the organization catalog on
        // completion.
        services.AddPostgresFeatureBulkImporter(connectionString);
    }

    public void UseMiddleware(IApplicationBuilder app)
    {
        // A read too large for a PostGIS-served organization catalog answers 422, not 500 (XD01-449).
        app.UseCatalogEnumerationLimitTranslation();
        app.UseOrganizationAssetCatalogs();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The WMS alias under the CORS-configured asset prefix (Blazor WASM clients), and the standalone OGC route; both
        // require features:read (XD01-96).
        endpoints.MapWmsApi(route: "/api/geoassets/wms");
        endpoints.MapWmsApi();

        // The authenticated MapLibre tile endpoints (XD01-434): /tiles/style.json and /tiles/{set}/{v}/{z}/{x}/{y}.mvt|.png,
        // features:read, without the organization catalog warm-up.
        endpoints.MapTileApi();
    }
}
