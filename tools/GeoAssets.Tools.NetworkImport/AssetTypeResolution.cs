using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// Which of the kernel's asset types an asset class lands on — the one rule <c>import</c> applies when it ensures the types
/// and <c>verify</c> applies when it checks them (XD01-449): the type with the class's role, else the one with the class's
/// own id, else one with a reuse role; none of them means <c>import</c> creates the class's own type.
/// </summary>
internal static class AssetTypeResolution
{
    public static AssetType? Existing(IReadOnlyList<AssetType> catalog, AssetClassDefinition assetClass)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(assetClass);

        return catalog.FirstOrDefault(type => type.Roles.Contains(assetClass.Role, StringComparer.Ordinal))
            ?? catalog.FirstOrDefault(type => type.Id == assetClass.TypeId)
            ?? catalog.FirstOrDefault(type => assetClass.ReuseRoles.Any(role => type.Roles.Contains(role, StringComparer.Ordinal)));
    }

    /// <summary>
    /// The kernel type id a planned feature should have: a plan carries each class's own <see cref="AssetClassDefinition.TypeId"/>
    /// (it never sees the kernel), which import may have mapped onto an existing type. Every built-in profile's classes are
    /// mapped; any other id is expected as it is.
    /// </summary>
    public static Func<string, string> PlannedToKernel(IReadOnlyList<AssetType> catalog)
    {
        var registry = BuiltInProfiles.CreateRegistry();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetClass in registry.ProfileIds.SelectMany(id => registry.Find(id).AssetClasses))
        {
            if (Existing(catalog, assetClass) is { } existing)
                map.TryAdd(assetClass.TypeId.ToString(), existing.Id.ToString());
        }

        return planned => map.TryGetValue(planned, out var kernel) ? kernel : planned;
    }
}
