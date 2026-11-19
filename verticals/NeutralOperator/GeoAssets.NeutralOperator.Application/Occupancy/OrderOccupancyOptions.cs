using GeoAssets.Core.Models;

namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>Host settings of <see cref="OrderOccupancyStagingService"/> (XD01-315).</summary>
public sealed class OrderOccupancyOptions
{
    /// <summary>
    /// Asset type ids whose features are poles in addition to every asset type carrying
    /// <see cref="PoleRole"/>, compared ignoring case. Deprecated (XD01-368): tag the asset type with
    /// the role instead; a host still fills this from its legacy configuration.
    /// </summary>
    public ISet<string> PoleAssetTypeIds { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The asset-type role that makes a feature a pole (XD01-368):
    /// <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/> unless the host says otherwise.
    /// </summary>
    public string PoleRole { get; set; } = OccupancyAssetTypeRoles.OccupiableSupport;

    /// <summary>
    /// The asset catalog instance that an approval's <c>occupancyClass</c> attribute updates are
    /// addressed to (XD01-318). <c>default</c> unless the host says otherwise.
    /// </summary>
    public string CatalogInstanceId { get; set; } = DefaultCatalogInstanceId;

    /// <summary>The catalog instance a host that configures none addresses.</summary>
    public const string DefaultCatalogInstanceId = "default";

    /// <summary>
    /// True when <paramref name="assetTypeId"/> is the id of one of <paramref name="assetTypes"/> carrying
    /// <see cref="PoleRole"/>, or one of <see cref="PoleAssetTypeIds"/>.
    /// </summary>
    public bool IsPole(string? assetTypeId, IEnumerable<AssetType> assetTypes)
    {
        ArgumentNullException.ThrowIfNull(assetTypes);
        if (assetTypeId is null) return false;
        if (PoleAssetTypeIds.Contains(assetTypeId)) return true;

        return assetTypes.Any(type =>
            string.Equals(type.Id.ToString(), assetTypeId, StringComparison.OrdinalIgnoreCase) && AssetTypeRoles.HasRole(type, PoleRole));
    }
}
