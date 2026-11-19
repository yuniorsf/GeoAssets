using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Persistence.Outbox;

namespace GeoAssets.Server;

/// <summary>
/// How this host applies the asset mutation batches its own outbox stages (XD01-337) — today the
/// <c>occupancyClass</c> attribute updates an occupancy approval stages (XD01-318) — when it has
/// no broker to relay them through.
/// </summary>
public static class InProcessAssetMutationHostExtensions
{
    /// <summary>The worker id the in-process relay claims outbox messages under.</summary>
    public const string RelayWorkerId = "geoassets-server-in-process";

    /// <summary>
    /// With <see cref="WorkflowMessagingHostExtensions.OutboxTransportKey"/> absent or
    /// <see cref="WorkflowMessagingHostExtensions.NoTransport"/>: runs the existing outbox relay
    /// (<see cref="OutboxRelayProcessor"/>) claiming
    /// <see cref="IntegrationEventTypes.AssetMutationBatchRequestedV1"/> messages only, with
    /// <see cref="InProcessAssetMutationBatchPublisher"/> as its "broker", which applies each batch to this host's catalog
    /// (<see cref="AssetCatalogConfiguration.InstanceId(IConfiguration)"/>; a warning is logged when it comes from the
    /// deprecated <see cref="AssetCatalogConfiguration.LegacyInstanceIdKey"/>) with
    /// <see cref="ObservableAssetMutationBatchApplier"/>'s span and metrics around the applier the catalog's provider
    /// registered as <see cref="IAssetMutationBatchApplier"/> (e.g. the PostgreSQL pushdown, XD01-374) — or, when none
    /// did, <see cref="CatalogMutationBatchApplier"/> over that catalog of the <see cref="ICatalogPool"/> (its default
    /// catalog when no catalog carries the instance id). Every other message type stays pending in the outbox, as before.
    /// The relay's options are its defaults (lease, exponential backoff, attempt limit), with <see cref="RelayWorkerId"/>.
    ///
    /// With a broker configured this registers nothing: the relay
    /// <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> set up publishes every
    /// message, asset batches included, exactly as before.
    ///
    /// Call after <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> (the outbox store
    /// and serializer) and after the host registered <see cref="GeoAssetsActivitySource"/> and
    /// <see cref="GeoAssetsMeter"/>.
    /// </summary>
    public static IServiceCollection AddServerInProcessAssetMutationApply(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (WorkflowMessagingHostExtensions.HasBroker(configuration))
        {
            return services;
        }

        var catalogInstanceId = AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey);

        services.AddSingleton<IIntegrationEventPublisher>(sp =>
        {
            if (fromLegacyKey)
            {
                AssetCatalogConfiguration.LegacyInstanceIdKeyUsed(
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InProcessAssetMutationHostExtensions).FullName!),
                    catalogInstanceId);
            }

            var applier = new ObservableAssetMutationBatchApplier(
                ApplierFor(sp, catalogInstanceId),
                sp.GetRequiredService<GeoAssetsActivitySource>(),
                sp.GetRequiredService<GeoAssetsMeter>(),
                sp.GetRequiredService<TimeProvider>());
            return new InProcessAssetMutationBatchPublisher(
                sp.GetRequiredService<IIntegrationMessageSerializer>(), applier, catalogInstanceId);
        });

        services.AddOptions<OutboxRelayOptions>().Configure(options =>
        {
            options.WorkerId = RelayWorkerId;
            options.MessageTypes.Add(IntegrationEventTypes.AssetMutationBatchRequestedV1);
        });
        services.AddHostedService<OutboxRelayProcessor>();
        return services;
    }

    /// <summary>
    /// The provider's applier when one is registered; otherwise the generic one over the catalog the batches are addressed
    /// to. It runs outside any request, so it has no applied-batch store of its own: a provider that needs idempotency
    /// registers its own applier.
    /// </summary>
    private static IAssetMutationBatchApplier ApplierFor(IServiceProvider sp, string catalogInstanceId)
    {
        if (sp.GetService<IAssetMutationBatchApplier>() is { } registered) return registered;

        var pool = sp.GetRequiredService<ICatalogPool>();
        var catalog = pool.TryGetCatalog(catalogInstanceId, out var addressed) ? addressed : pool.DefaultCatalog;
        return new CatalogMutationBatchApplier(catalog, store: null, sp.GetRequiredService<TimeProvider>());
    }
}
