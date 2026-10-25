namespace GeoAssets.Core.Models;

/// <summary>Layer visibility/order overrides for a <see cref="Project"/>.</summary>
public sealed class ProjectLayerScope
{
    /// <summary>Empty means defer entirely to normal <see cref="Layer"/>/<see cref="LayerRule"/> resolution.</summary>
    public List<ProjectLayerOverride> Overrides { get; init; } = [];
}
