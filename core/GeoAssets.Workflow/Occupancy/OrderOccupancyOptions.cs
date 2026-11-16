namespace GeoAssets.Workflow.Occupancy;

/// <summary>Host settings of <see cref="OrderOccupancyStagingService"/> (XD01-315).</summary>
public sealed class OrderOccupancyOptions
{
    /// <summary>
    /// The asset type ids whose features are poles, compared ignoring case. The asset-type
    /// catalog has no fixed "pole" type — each deployment names its own — so a target counts as
    /// a pole only when its asset type is listed here. Empty (the default) means no target is.
    /// </summary>
    public ISet<string> PoleAssetTypeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="assetTypeId"/> is one of <see cref="PoleAssetTypeIds"/>.</summary>
    public bool IsPole(string? assetTypeId) => assetTypeId is not null && PoleAssetTypeIds.Contains(assetTypeId);
}
