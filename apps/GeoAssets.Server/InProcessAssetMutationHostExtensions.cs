using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.PostgreSQL.Repositories;
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
    /// <see cref="InProcessAssetMutationBatchPublisher"/> as its "broker", which applies each batch
    /// to this host's catalog (<see cref="AssetCatalogConfiguration.InstanceId(IConfiguration)"/>; a warning is logged
    /// when it comes from the deprecated <see cref="AssetCatalogConfiguration.LegacyInstanceIdKey"/>) through
    /// <see cref="PostgresAssetMutationBatchApplier"/> on <paramref name="connectionString"/>,
    /// with <see cref="ObservableAssetMutationBatchApplier"/>'s span and metrics. Every other
    /// message type stays pending in the outbox, as before. The relay's options are its defaults
    /// (lease, exponential backoff, attempt limit), with <see cref="RelayWorkerId"/>.
    ///
    /// With a broker configured this registers nothing: the relay
    /// <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> set up publishes every
    /// message, asset batches included, exactly as before.
    ///
    /// Call after <see cref="WorkflowMessagingHostExtensions.AddServerWorkflowMessaging"/> (the outbox store
    /// and serializer) and after the host registered <see cref="GeoAssetsActivitySource"/> and
    /// <see cref="GeoAssetsMeter"/>.
    /// </summary>
    public static IServiceCollection AddServerInProcessAssetMutationApply(
        this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        if (WorkflowMessagingHostExtensions.HasBroker(configuration))
        {
            return services;
        }

        var catalogInstanceId = AssetCatalogConfiguration.InstanceId(configuration, out var fromLegacyKey);

        services.AddPostgresAssetMutationBatchApplier(connectionString);
        services.AddSingleton<IAssetMutationBatchApplier>(sp => new ObservableAssetMutationBatchApplier(
            RefreshingTheOrganizationCatalogs(sp, sp.GetRequiredService<PostgresAssetMutationBatchApplier>()),
            sp.GetRequiredService<GeoAssetsActivitySource>(),
            sp.GetRequiredService<GeoAssetsMeter>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IIntegrationEventPublisher>(sp =>
        {
            if (fromLegacyKey)
            {
                AssetCatalogConfiguration.LegacyInstanceIdKeyUsed(
                    sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InProcessAssetMutationHostExtensions).FullName!),
                    catalogInstanceId);
            }

            return new InProcessAssetMutationBatchPublisher(
                sp.GetRequiredService<IIntegrationMessageSerializer>(),
                sp.GetRequiredService<IAssetMutationBatchApplier>(),
                catalogInstanceId);
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
    /// <paramref name="applier"/>, followed by a reload of the batch's organization's cached
    /// catalog (XD01-338) when the host serves organization-scoped catalogs
    /// (<see cref="OrganizationAssetCatalogHostExtensions.AddServerOrganizationAssetCatalogs"/>).
    /// </summary>
    private static IAssetMutationBatchApplier RefreshingTheOrganizationCatalogs(IServiceProvider sp, IAssetMutationBatchApplier applier) =>
        sp.GetService<PostgresOrganizationAssetCatalogs>() is { } catalogs
            ? new OrganizationCatalogRefreshingApplier(applier, catalogs)
            : applier;
}
