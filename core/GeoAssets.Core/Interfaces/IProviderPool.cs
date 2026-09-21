using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Manages a pool of named <see cref="ProviderEntry"/> instances.
/// One entry is always "active" — all UI writes (AssetForm, AssetList, Import) target it.
/// Other entries can be opened on the map as read-only overlays.
/// </summary>
public interface IProviderPool
{
    IReadOnlyList<ProviderEntry> All    { get; }
    ProviderEntry                Active { get; }

    /// <summary>
    /// Wraps an externally created provider (e.g. PostgreSQL-backed) in a pool entry.
    /// Use this to connect any <see cref="IAssetProvider"/> implementation to the map.
    /// <paramref name="pluginId"/>/<paramref name="values"/> are the reconnect config the
    /// provider was created from (see <see cref="ProviderEntry.PluginId"/>/<see cref="ProviderEntry.Values"/>)
    /// — omit only when there is no known plugin (e.g. tests).
    /// </summary>
    ProviderEntry Add(string name, IAssetProvider provider, string pluginId = "", Dictionary<string, string>? values = null);

    /// <summary>
    /// Wraps an externally created provider in a pool entry with its full state restored in
    /// one call — the reconnect-loop counterpart to <see cref="Add"/>, avoiding the visible
    /// intermediate states of <see cref="Add"/> followed by separate <see cref="SetActive"/>/
    /// <see cref="Open"/>/<see cref="Close"/>/<see cref="Enable"/>/<see cref="Disable"/> calls.
    /// </summary>
    ProviderEntry RestoreEntry(
        string name, IAssetProvider provider, int position,
        bool isOpen, bool isEnabled, bool isActive,
        string pluginId = "", Dictionary<string, string>? values = null);

    /// <summary>
    /// Snapshots every entry's persistable state — name, plugin, reconnect values, position
    /// (by <see cref="All"/> order), and open/enabled/active flags — into
    /// <see cref="ProjectProviderEntry"/> rows. The <see cref="RestoreEntry"/> reconnect loop's
    /// counterpart; used to materialize <c>Project.Providers</c> from live pool state for saving.
    /// </summary>
    List<ProjectProviderEntry> ToPersistedEntries();

    /// <summary>Makes the given entry the active workspace; opens and enables it if needed.</summary>
    void SetActive(Guid id);

    /// <summary>Marks the entry as open on the map (caller is responsible for rendering).</summary>
    void Open(Guid id);

    /// <summary>Marks the entry as closed (caller is responsible for removing from map).</summary>
    void Close(Guid id);

    /// <summary>Makes features of an open entry visible on the map.</summary>
    void Enable(Guid id);

    /// <summary>Hides features of an open entry from the map without closing it.</summary>
    void Disable(Guid id);

    /// <summary>Marks every current entry as open on the map.</summary>
    void OpenAll();

    /// <summary>Marks every current entry as closed.</summary>
    void CloseAll();

    /// <summary>Makes features of every open entry visible on the map.</summary>
    void EnableAll();

    /// <summary>Hides features of every entry from the map without closing them.</summary>
    void DisableAll();

    void Rename(Guid id, string name);

    /// <summary>Removes the entry from the pool. The active entry cannot be removed.</summary>
    void Remove(Guid id);

    /// <summary>
    /// Removes every entry unconditionally, including the active one — unlike <see cref="Remove"/>.
    /// For a caller about to repopulate the whole pool from scratch (e.g. <c>IProjectSessionService</c>
    /// switching Projects or discarding changes via a fresh <see cref="RestoreEntry"/> pass), so
    /// entries from the previous state aren't left stranded alongside the new ones.
    /// </summary>
    void ClearAll();

    /// <summary>Fires whenever pool state changes (entry added, removed, or state updated).</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Fires when a new entry is added via <see cref="Add"/>, with that entry. Kept distinct
    /// from <see cref="Changed"/> (which also fires on <see cref="SetActive"/>/<see cref="Enable"/>/
    /// etc.) so a listener that only cares about newly-connected providers — e.g. rendering the
    /// initial map layer — doesn't double-fire on unrelated mutations.
    /// </summary>
    event EventHandler<ProviderEntry>? EntryAdded;
}
