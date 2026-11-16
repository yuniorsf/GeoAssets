using GeoAssets.Workflow;
using GeoAssets.Workflow.Occupancy;

namespace GeoAssets.Server;

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

        var configured = (configuration.GetSection(PoleAssetTypeIdsKey).Get<string[]>() ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToList();

        return services.AddOrderOccupancyStaging(options =>
        {
            foreach (var poleAssetTypeId in configured.Count > 0 ? configured : [SeededPoleAssetTypeId])
                options.PoleAssetTypeIds.Add(poleAssetTypeId);
        });
    }
}
