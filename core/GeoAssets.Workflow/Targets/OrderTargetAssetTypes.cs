using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The asset types an <see cref="OrderTargetPolicy"/> targets (XD01-368): its
/// <see cref="OrderTargetPolicy.TargetAssetTypeIds"/> plus every asset type carrying one of its
/// <see cref="OrderTargetPolicy.TargetAssetTypeRoles"/> (<see cref="AssetType.Roles"/>, ordinal) —
/// resolved against the catalog's asset types when a selection is evaluated, so a type tagged with
/// a targeted role later is targeted without changing the order type.
/// </summary>
public static class OrderTargetAssetTypes
{
    /// <summary>The targeted asset type ids, distinct (ignoring case), the policy's own ids first.</summary>
    public static IReadOnlyList<string> Resolve(OrderTargetPolicy policy, IEnumerable<AssetType> assetTypes)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(assetTypes);

        var resolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in policy.TargetAssetTypeIds)
        {
            if (seen.Add(id)) resolved.Add(id);
        }

        if (policy.TargetAssetTypeRoles.Count == 0) return resolved;

        foreach (var assetType in assetTypes)
        {
            var id = assetType.Id.ToString();
            if (policy.TargetAssetTypeRoles.Any(role => AssetTypeRoles.HasRole(assetType, role)) && seen.Add(id))
                resolved.Add(id);
        }

        return resolved;
    }
}
