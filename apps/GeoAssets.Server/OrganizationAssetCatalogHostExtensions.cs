using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Providers;

namespace GeoAssets.Server;

/// <summary>
/// Organization-scoped asset reads for this host (XD01-338, XD01-373): every request's <see cref="IAssetProvider"/> is the
/// catalog as the caller's organization sees it — its own features plus the shared (organization-less) ones — through
/// the <see cref="IOrganizationCatalogFactory"/> port, whatever provider backs the catalog.
/// </summary>
public static partial class OrganizationAssetCatalogHostExtensions
{
    /// <summary>
    /// The generic organization scoping, for a host whose provider registered no native <see cref="IOrganizationCatalogFactory"/>
    /// (a module that has one — e.g. the PostgreSQL one — registers it first, and this does nothing): the host's singleton
    /// <see cref="IAssetProvider"/> becomes the shared catalog, <see cref="FilteringOrganizationCatalogFactory"/> the factory,
    /// and each request's <see cref="IAssetProvider"/> its view for the request's <see cref="ITenantContext"/> (the shared
    /// features alone without one). Call after enabling the modules.
    /// </summary>
    public static IServiceCollection AddServerOrganizationAssetCatalogs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(IOrganizationCatalogFactory))) return services;

        services.MoveSharedAssetCatalogToKey();
        services.AddSingleton<IOrganizationCatalogFactory>(sp =>
            new FilteringOrganizationCatalogFactory(sp.GetRequiredKeyedService<IAssetProvider>(OrganizationCatalogKeys.SharedCatalog)));
        services.AddScoped(sp => sp.GetRequiredService<IOrganizationCatalogFactory>()
            .GetForOrganization(sp.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty));
        return services;
    }

    /// <summary>
    /// Loads the calling organization's catalog (<see cref="IOrganizationCatalogFactory.GetForOrganizationAsync"/>) before the
    /// request reaches its endpoint, so the endpoint's synchronous reads (<c>GetById</c>, <c>GetAll</c>, …) find it in
    /// memory rather than blocking on its first load. Runs for every request whose caller has an organization (asset reads
    /// also happen behind the workflow routes); after the first request it is a lookup. A failed load is logged and the
    /// request goes on: the endpoint then loads the catalog itself and fails there if the store does. Call after
    /// authentication, so the caller is known. Does nothing when no <see cref="IOrganizationCatalogFactory"/> is registered.
    /// </summary>
    public static IApplicationBuilder UseOrganizationAssetCatalogs(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use(WarmCallersCatalogAsync);
    }

    private static async Task WarmCallersCatalogAsync(HttpContext context, RequestDelegate next)
    {
        var catalogs = context.RequestServices.GetService<IOrganizationCatalogFactory>();
        var organizationId = context.RequestServices.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty;

        if (catalogs is not null && organizationId != Guid.Empty)
        {
            try
            {
                await catalogs.GetForOrganizationAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
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
