namespace GeoAssets.Core.Models;

/// <summary>
/// Overrides visibility/order on an existing <see cref="Layer"/> (via its persisted row). v1 only
/// supports overriding an existing layer — no inline/ad hoc Project-local layers.
/// </summary>
public sealed class ProjectLayerOverride
{
    public Guid LayerId { get; set; }
    public bool Visible { get; set; }
    public int SortOrder { get; set; }
}
