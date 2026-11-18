using GeoAssets.Core.Interfaces;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Server.Modules.PostgresSpatial;

/// <summary>
/// The PostgreSQL/PostGIS spatial plane as a module of this host (XD01-359, ADR-001 Phase 1): every Postgres-concrete
/// registration <c>Program.cs</c> made by hand — the warmed asset provider, organization-scoped catalogs and their
/// middleware, the PostGIS spatial query, the <see cref="GeoAssetsDbContext"/> factory, the applied-batch store and the
/// WMS renderer with both WMS routes — moved unchanged and in the same order. Without it the kernel boots on whatever
/// <see cref="IAssetProvider"/> the host registers (invariant I5). Phase 4 replaces it with a provider plugin.
/// </summary>
public sealed class PostgresSpatialModule : IGeoAssetsModule, IModuleServiceInitializer, IModuleMiddleware
{
    public const string ModuleId = "postgres-spatial";

    /// <summary>Configuration key: whether the WMS routes require an authenticated caller (default <see langword="false"/>, XD01-95).</summary>
    public const string WmsRequireAuthenticationKey = "Wms:RequireAuthentication";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddGeoAssetsPostgres();
    }

    public async Task ConfigureServicesAsync(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException($"The '{ModuleId}' module needs the 'GeoAssets' connection string.");

        // Pre-warms the IAssetProvider singleton before the host runs — otherwise PostgresAssetProvider's ISyncProvider
        // marker (XD01-161) would be dishonest: the first GetAll()/GetById() call would block on LoadCacheFromDb (XD01-173).
        await services.AddWarmedPostgresAssetProviderAsync(connectionString, cancellationToken).ConfigureAwait(false);

        // Organization-scoped asset reads (XD01-338): the warmed provider is the shared catalog, each request's
        // IAssetProvider is the caller's organization's view.
        services.AddServerOrganizationAssetCatalogs(connectionString);

        // IAssetSpatialQuery (XD01-307): area/attribute selection pushed down to PostGIS, scoped to the request's tenant.
        services.AddPostgresAssetSpatialQuery(connectionString);

        // IDbContextFactory<GeoAssetsDbContext>: each WMS tile request opens its own short-lived DbContext.
        services.AddGeoAssetsDbContextFactory(connectionString);

        // IAppliedMutationBatchStore (XD01-229) over the same factory, a scoped per-request DbContext.
        services.AddScoped<IAppliedMutationBatchStore>(sp =>
            new EFAppliedMutationBatchStore(sp.GetRequiredService<IDbContextFactory<GeoAssetsDbContext>>().CreateDbContext()));

        // WMS renderer — queries PostGIS directly (geometry + color projection only).
        services.AddSingleton<WmsPostGisRenderer>();
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
