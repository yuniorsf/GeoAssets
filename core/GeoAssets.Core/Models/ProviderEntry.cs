using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Models;

/// <summary>
/// Represents one named collection in the provider pool.
/// Each entry owns an independent <see cref="IAssetProvider"/> and carries
/// its display state (open on map, visible, active for editing).
/// </summary>
public sealed class ProviderEntry
{
    public Guid   Id        { get; }      = Guid.NewGuid();
    public string Name      { get; set; } = string.Empty;

    /// <summary>Features from this entry are currently rendered on the map.</summary>
    public bool IsOpen    { get; set; }

    /// <summary>Features are visible on the map (only meaningful when <see cref="IsOpen"/>).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>This is the editable workspace; all writes from the UI target this entry.</summary>
    public bool IsActive  { get; set; }

    public IAssetProvider Provider { get; init; } = null!;
}
