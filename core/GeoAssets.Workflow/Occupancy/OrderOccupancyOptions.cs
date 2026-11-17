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

    /// <summary>
    /// The asset catalog instance that an approval's <c>occupancyClass</c> attribute updates are
    /// addressed to (XD01-318). <c>default</c> unless the host says otherwise.
    /// </summary>
    public string CatalogInstanceId { get; set; } = DefaultCatalogInstanceId;

    /// <summary>The catalog instance a host that configures none addresses.</summary>
    public const string DefaultCatalogInstanceId = "default";

    /// <summary>True when <paramref name="assetTypeId"/> is one of <see cref="PoleAssetTypeIds"/>.</summary>
    public bool IsPole(string? assetTypeId) => assetTypeId is not null && PoleAssetTypeIds.Contains(assetTypeId);
}
