using GeoAssets.NeutralOperator.Application;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Modules.NeutralOperator;

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
    /// <c>ServiceOrderDbContext</c> stores, its unit of work and its outbox, with its span and
    /// metrics. Call after <see cref="AddServerOrderOccupancyStaging"/> (whose options it reads)
    /// and <c>AddServerWorkflowOutbox</c>.
    /// </summary>
    public static IServiceCollection AddServerOccupancyCommit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The approval that commits staged occupancy is this module's order action handler (XD01-360).
        services.AddScoped<IOrderActionHandler, OccupancyApprovalHandler>();

        return services
            .AddOccupancyCommit()
            .AddOccupancyCommitPersistence();
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
