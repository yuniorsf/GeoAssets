namespace GeoAssets.Core.Models;

/// <summary>
/// A reconnectable reference to one supplemental provider entry, as persisted on a <see cref="Project"/>.
/// </summary>
public sealed class ProjectProviderEntry
{
    /// <summary>Stable identity for this entry across saves. Generated once at creation — unlike the
    /// runtime <see cref="ProviderEntry.Id"/>, which regenerates on every construction, so it cannot be
    /// used to correlate a saved entry back to itself after a reconnect.</summary>
    public Guid EntryId { get; set; } = Guid.NewGuid();

    /// <summary>Explicit order among sibling entries. Don't rely on JSON array order — entries can be
    /// added/removed/reordered independently, so array position isn't a contract worth depending on.</summary>
    public int Position { get; set; }

    public string Name { get; set; } = string.Empty;
    public string PluginId { get; set; } = string.Empty;

    /// <summary>
    /// Reconnect configuration for this entry — same shape as <c>BootLoaderService.PersistedBootConfig.Values</c>.
    /// Credentials are never persisted here: by the time this is saved, keys ending in <c>_content</c> and
    /// any key backing a password-shaped <c>ProviderConfigField</c> (<c>ProviderFieldType.Password</c>) must
    /// already be stripped — the user re-enters them on reconnect.
    /// </summary>
    public Dictionary<string, string> Values { get; init; } = [];

    /// <summary>Mirrors <see cref="ProviderEntry.IsOpen"/>.</summary>
    public bool IsOpen { get; set; }

    /// <summary>Mirrors <see cref="ProviderEntry.IsEnabled"/>.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Mirrors <see cref="ProviderEntry.IsActive"/>.</summary>
    public bool IsActive { get; set; }
}
