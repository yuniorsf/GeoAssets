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

    /// <summary>Explicit order of this entry within the pool.</summary>
    public int Position { get; set; }

    /// <summary>Features from this entry are currently rendered on the map.</summary>
    public bool IsOpen    { get; set; }

    /// <summary>Features are visible on the map (only meaningful when <see cref="IsOpen"/>).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>This is the editable workspace; all writes from the UI target this entry.</summary>
    public bool IsActive  { get; set; }

    public IAssetProvider Provider { get; init; } = null!;

    /// <summary>Plugin slug (<see cref="Interfaces.IProviderPlugin.Id"/>) this entry was
    /// constructed from. Empty for entries added without a known plugin.</summary>
    public string PluginId { get; init; } = string.Empty;

    /// <summary>
    /// Reconnect configuration this entry was constructed from — same credential-free shape as
    /// <see cref="ProjectProviderEntry.Values"/>, which this mirrors so <see cref="Interfaces.IProviderPool.ToPersistedEntries"/>
    /// can copy it directly.
    /// </summary>
    public Dictionary<string, string> Values { get; init; } = [];
}
