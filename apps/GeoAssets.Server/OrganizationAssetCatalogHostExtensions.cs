using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Repositories;

namespace GeoAssets.Server;

/// <summary>
/// Organization-scoped asset reads for this host (XD01-338): every request's
/// <see cref="IAssetProvider"/> is the catalog as the caller's organization sees it — its own
/// features plus the shared (organization-less) ones — instead of the tenant-less singleton,
/// which only ever saw the shared ones.
/// </summary>
public static partial class OrganizationAssetCatalogHostExtensions
{
    /// <summary>
    /// <see cref="PostgresOrganizationCatalogServiceExtensions.AddPostgresOrganizationAssetCatalogs"/>
    /// on <paramref name="connectionString"/>. Call after the host's singleton <see cref="IAssetProvider"/> is
    /// registered — the catalog pool's default catalog (<c>AddServerCatalogPool</c>, XD01-371) — which becomes the
    /// shared catalog.
    /// </summary>
    public static IServiceCollection AddServerOrganizationAssetCatalogs(this IServiceCollection services, string connectionString) =>
        services.AddPostgresOrganizationAssetCatalogs(connectionString);

    /// <summary>
    /// Loads the calling organization's catalog before the request reaches its endpoint, so the
    /// endpoint's synchronous reads (<c>GetById</c>, <c>GetAll</c>, …) find it in memory rather than
    /// blocking on its first load. Runs for every request whose caller has an organization (asset
    /// reads also happen behind the workflow routes); after the first request it is a lookup.
    /// A failed load is logged and the request goes on: the endpoint then loads the catalog itself
    /// and fails there if the database does. Call after authentication, so the caller is known.
    /// Does nothing when <see cref="AddServerOrganizationAssetCatalogs"/> was not called.
    /// </summary>
    public static IApplicationBuilder UseOrganizationAssetCatalogs(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(WarmCallersCatalogAsync);
    }

    private static async Task WarmCallersCatalogAsync(HttpContext context, RequestDelegate next)
    {
        var catalogs = context.RequestServices.GetService<PostgresOrganizationAssetCatalogs>();
        var organizationId = context.RequestServices.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty;

        if (catalogs is not null && organizationId != Guid.Empty)
        {
            try
            {
                await catalogs.WarmAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(OrganizationAssetCatalogHostExtensions).FullName!);
                LogWarmFailed(logger, organizationId, ex);
            }
        }

        await next(context).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading the asset catalog of organization {OrganizationId} failed; the request continues without it")]
    private static partial void LogWarmFailed(ILogger logger, Guid organizationId, Exception ex);
}
