using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server;

/// <summary>
/// The kernel side of the asset-mutation apply saga (XD01-383, ADR-001 Phase 7) — what replaced
/// <c>AddServerInProcessAssetMutationApply</c> (XD01-337): one consumer of every vertical's
/// <see cref="IntegrationEventTypes.AssetMutationBatchRequestedV1"/>, the same with or without a broker. Domain-neutral and
/// module-free: it is registered with <c>GeoAssets:Modules = []</c> too, and then simply never receives anything.
/// </summary>
public static class AssetMutationApplyHostExtensions
{
    /// <summary>
    /// Registers <see cref="AssetMutationBatchApplyHandler"/> on this host's transport
    /// (<see cref="IntegrationConsumerHostExtensions.AddWorkflowIntegrationConsumer{TEvent, THandler}"/>):
    /// <list type="bullet">
    /// <item><description>a broker whose consumer section names a topic: a broker consumer of its own
    /// (<see cref="AssetMutationBatchApplyHandler.ConsumerKey"/>), fenced on this host's catalog instance;</description></item>
    /// <item><description>a broker with no topic for it: nothing — the batches wait on the broker, and the requesting
    /// vertical's saga times out and alerts;</description></item>
    /// <item><description>no broker: in process — this host's outbox relay hands the batches its own modules stage to the
    /// handler, and the handler's replies to the modules' saga consumers, in the same way.</description></item>
    /// </list>
    /// The applier is <see cref="KernelAssetMutationApplier"/>'s, for this host's catalog
    /// (<see cref="AssetCatalogConfiguration.InstanceId(IConfiguration)"/>; a warning is logged when it comes from the
    /// deprecated <see cref="AssetCatalogConfiguration.LegacyInstanceIdKey"/>).
    ///
    /// Call after <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> (outbox, inbox, serializer) and the
    /// catalog pool, and with <see cref="GeoAssetsActivitySource"/> and <see cref="GeoAssetsMeter"/> registered.
    /// </summary>
    public static IServiceCollection AddServerAssetMutationApply(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var catalogInstanceId = AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey);

        services.TryAddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
        services.TryAddScoped<IWorkflowUnitOfWork>(sp => new EFWorkflowUnitOfWork(sp.GetRequiredService<ServiceOrderDbContext>()));
        services.TryAddSingleton(sp =>
        {
            if (fromLegacyKey)
            {
                AssetCatalogConfiguration.LegacyInstanceIdKeyUsed(
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AssetMutationApplyHostExtensions).FullName!),
                    catalogInstanceId);
            }

            return new KernelAssetMutationApplier(
                catalogInstanceId,
                new ObservableAssetMutationBatchApplier(
                    ApplierFor(sp, catalogInstanceId),
                    sp.GetRequiredService<GeoAssetsActivitySource>(),
                    sp.GetRequiredService<GeoAssetsMeter>(),
                    sp.GetRequiredService<TimeProvider>()));
        });

        return services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, AssetMutationBatchApplyHandler>(
            configuration,
            new IntegrationConsumerRegistration(
                AssetMutationBatchApplyHandler.ConsumerKey,
                AssetMutationBatchApplyHandler.ConsumerName,
                IntegrationEventTypes.AssetMutationBatchRequestedV1)
            {
                TargetCatalogInstanceId = catalogInstanceId,
            });
    }

    /// <summary>
    /// The provider's applier when one is registered; otherwise the generic one over the catalog the batches are addressed
    /// to (the pool's default catalog when no catalog carries the instance id). The generic one has no applied-batch store
    /// of its own: a provider that needs idempotency registers its own applier.
    /// </summary>
    private static IAssetMutationBatchApplier ApplierFor(IServiceProvider sp, string catalogInstanceId)
    {
        if (sp.GetService<IAssetMutationBatchApplier>() is { } registered) return registered;

        var pool = sp.GetRequiredService<ICatalogPool>();
        var catalog = pool.TryGetCatalog(catalogInstanceId, out var addressed) ? addressed : pool.DefaultCatalog;
        return new CatalogMutationBatchApplier(catalog, store: null, sp.GetRequiredService<TimeProvider>());
    }
}
