using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Manages a pool of named <see cref="RepositoryEntry"/> instances.
/// One entry is always "active" — all UI writes (AssetForm, AssetList, Import) target it.
/// Other entries can be opened on the map as read-only overlays.
/// </summary>
public interface IRepositoryPool
{
    IReadOnlyList<RepositoryEntry> All    { get; }
    RepositoryEntry                Active { get; }

    /// <summary>Creates a new entry and adds it to the pool (closed by default).</summary>
    RepositoryEntry Add(string name);

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

    void Rename(Guid id, string name);

    /// <summary>Removes the entry from the pool. The active entry cannot be removed.</summary>
    void Remove(Guid id);

    /// <summary>Fires whenever pool state changes (entry added, removed, or state updated).</summary>
    event EventHandler? Changed;
}
