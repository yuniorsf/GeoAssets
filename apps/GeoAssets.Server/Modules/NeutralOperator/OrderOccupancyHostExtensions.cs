using GeoAssets.Workflow;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// This host's registration of staged occupancy changes (XD01-315): it decides which asset
/// types are poles, which <see cref="OrderOccupancyOptions"/> leaves to the host because the
/// asset-type catalog has no fixed "pole" type.
/// </summary>
public static class OrderOccupancyHostExtensions
{
    /// <summary>Configuration key of the pole asset type ids (an array of strings).</summary>
    public const string PoleAssetTypeIdsKey = "Workflow:Occupancy:PoleAssetTypeIds";

    /// <summary>
    /// Deprecated (XD01-357): the catalog instance id is the host's <see cref="AssetCatalogConfiguration.InstanceIdKey"/>;
    /// this key is still read as its fallback (<see cref="AssetCatalogConfiguration.LegacyInstanceIdKey"/>).
    /// </summary>
    public const string CatalogInstanceIdKey = AssetCatalogConfiguration.LegacyInstanceIdKey;

    /// <summary>
    /// Id of the pole asset type of the global domain catalog this host's database is seeded
    /// with (the PostgreSQL provider's <c>SeedDomainAssetTypesAndLayers</c> migration) — what a
    /// deployment that configures nothing treats as "a pole".
    /// </summary>
    public const string SeededPoleAssetTypeId = "00000000-0000-0000-0000-000000000004";

    /// <summary>
    /// Registers <see cref="IOrderOccupancyStagingService"/> with the pole asset types listed
    /// under <see cref="PoleAssetTypeIdsKey"/>, or <see cref="SeededPoleAssetTypeId"/> alone when
    /// the key is absent or empty. Blank entries are ignored.
    /// </summary>
    public static IServiceCollection AddServerOrderOccupancyStaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var poleAssetTypeIds = PoleAssetTypeIds(configuration);
        var catalogInstanceId = CatalogInstanceId(configuration);

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
    /// The asset types this host treats as poles: the configured <see cref="PoleAssetTypeIdsKey"/>
    /// when it lists any, otherwise <see cref="SeededPoleAssetTypeId"/> alone. Also what the
    /// shared-use order types are registered with (XD01-316), so "a pole" means one thing here.
    /// </summary>
    public static IReadOnlyList<string> PoleAssetTypeIds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = (configuration.GetSection(PoleAssetTypeIdsKey).Get<string[]>() ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        return configured.Count > 0 ? configured : [SeededPoleAssetTypeId];
    }
}
