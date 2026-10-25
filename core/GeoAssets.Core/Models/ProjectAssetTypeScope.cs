namespace GeoAssets.Core.Models;

/// <summary>Asset-type visibility scope for a <see cref="Project"/>.</summary>
public sealed class ProjectAssetTypeScope
{
    /// <summary>Empty means all asset types are visible (today's default behavior).</summary>
    public List<Guid> VisibleAssetTypeIds { get; init; } = [];
}
