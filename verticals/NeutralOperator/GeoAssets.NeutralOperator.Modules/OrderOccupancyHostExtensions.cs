using GeoAssets.NeutralOperator.Application;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Modules.Occupancy;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// This host's registration of staged occupancy changes (XD01-315). A target is a pole when its
/// asset type carries <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/> (XD01-368); the
/// deprecated <see cref="PoleAssetTypeIdsKey"/> is still honoured, with a warning at startup.
/// </summary>
public static class OrderOccupancyHostExtensions
{
    /// <summary>
    /// Deprecated (XD01-368) configuration key of extra pole asset type ids (an array of strings): tag the asset type
    /// with <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/> instead. Still honoured when set, and logged.
    /// </summary>
    public const string PoleAssetTypeIdsKey = "Workflow:Occupancy:PoleAssetTypeIds";

    /// <summary>
    /// Deprecated (XD01-357): the catalog instance id is the host's <see cref="AssetCatalogConfiguration.InstanceIdKey"/>;
    /// this key is still read as its fallback (<see cref="AssetCatalogConfiguration.LegacyInstanceIdKey"/>).
    /// </summary>
    public const string CatalogInstanceIdKey = AssetCatalogConfiguration.LegacyInstanceIdKey;

    /// <summary>
    /// Registers <see cref="IOrderOccupancyStagingService"/>: poles are the asset types carrying
    /// <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/>, plus any listed under the deprecated
    /// <see cref="PoleAssetTypeIdsKey"/> (blank entries ignored), which is then logged at startup.
    /// </summary>
    public static IServiceCollection AddServerOrderOccupancyStaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var poleAssetTypeIds = PoleAssetTypeIds(configuration);
        var catalogInstanceId = CatalogInstanceId(configuration);

        if (poleAssetTypeIds.Count > 0)
            services.AddHostedService(sp => new DeprecatedPoleAssetTypeIdsWarning(
                sp.GetRequiredService<ILogger<DeprecatedPoleAssetTypeIdsWarning>>(), poleAssetTypeIds));

        // The staged-occupancy routes' problem answers (XD01-363).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProblemDetailsMapper, OccupancyProblemMapper>());

        return services.AddOrderOccupancyStaging(options =>
        {
            foreach (var poleAssetTypeId in poleAssetTypeIds)
                options.PoleAssetTypeIds.Add(poleAssetTypeId);
            options.CatalogInstanceId = catalogInstanceId;
        });
    }

    /// <summary>
    /// Registers the approval that commits staged occupancy (XD01-318) over this host's
    /// <c>NeutralOperatorDbContext</c> stores, joined to the kernel <c>ServiceOrderDbContext</c>'s
    /// unit of work and outbox (XD01-380), with its span and metrics — and its saga (XD01-383): the consumers of the kernel's
    /// <see cref="IntegrationEventTypes.AssetMutationBatchAppliedV1"/> and <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/>
    /// on this host's transport (a broker consumer each when a broker names a topic for them, in process when the host has
    /// no broker — <see cref="IntegrationConsumerHostExtensions.AddWorkflowIntegrationConsumer{TEvent, THandler}"/>), and the
    /// timeout sweeper (<see cref="OccupancyApplySagaOptions"/> from <c>Saga</c>). Call after
    /// <see cref="AddServerOrderOccupancyStaging"/> (whose options it reads) and <c>AddServerWorkflowMessaging</c>.
    /// </summary>
    public static IServiceCollection AddServerOccupancyCommit(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The approval that commits staged occupancy is this module's order action handler (XD01-360).
        services.AddScoped<IOrderActionHandler, OccupancyApprovalHandler>();

        services.AddOccupancyCommit().AddOccupancyCommitPersistence();

        // The approval saga's replies, deduplicated by the saga's own inbox records (XD01-383).
        services.TryAddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
        services.AddWorkflowIntegrationConsumer<AssetBatchAppliedEvent, OccupancyApplyAppliedHandler>(
            configuration,
            new IntegrationConsumerRegistration(
                OccupancyApplySagaService.AppliedConsumerKey,
                OccupancyApplySagaService.AppliedConsumerName,
                IntegrationEventTypes.AssetMutationBatchAppliedV1));
        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRejectedEvent, OccupancyApplyRejectedHandler>(
            configuration,
            new IntegrationConsumerRegistration(
                OccupancyApplySagaService.RejectedConsumerKey,
                OccupancyApplySagaService.RejectedConsumerName,
                IntegrationEventTypes.AssetMutationBatchRejectedV1));

        // The saga timeout: flagged, counted, alerted on — never reverted silently.
        services.AddOptions<OccupancyApplySagaOptions>()
            .Bind(configuration.GetSection(OccupancyApplySagaOptions.SectionName))
            .Validate(
                o => o.ApplyTimeout > TimeSpan.Zero && o.SweepInterval > TimeSpan.Zero && o.SweepBatchSize > 0 && o.MaxPagesPerSweep > 0,
                $"{OccupancyApplySagaOptions.SectionName}: ApplyTimeout and SweepInterval must be positive, SweepBatchSize and MaxPagesPerSweep at least 1.")
            .ValidateOnStart();
        services.AddHostedService<OccupancyApplySagaSweeper>();

        return services;
    }

    /// <summary>
    /// The catalog instance an approval's attribute updates are addressed to: the host's
    /// <see cref="AssetCatalogConfiguration.InstanceId(IConfiguration)"/> (XD01-357), so the batches staged here and the
    /// catalog the host applies them to itself when it has no broker (XD01-337) always agree.
    /// </summary>
    public static string CatalogInstanceId(IConfiguration configuration) => AssetCatalogConfiguration.InstanceId(configuration);

    /// <summary>
    /// The pole asset type ids configured under the deprecated <see cref="PoleAssetTypeIdsKey"/>, trimmed, blank entries
    /// ignored; empty when the key is absent — the <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/> role then
    /// alone says which asset types are poles.
    /// </summary>
    public static IReadOnlyList<string> PoleAssetTypeIds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return (configuration.GetSection(PoleAssetTypeIdsKey).Get<string[]>() ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();
    }
}
