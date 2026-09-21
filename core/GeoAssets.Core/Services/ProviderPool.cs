using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// General-purpose implementation of <see cref="IProviderPool"/>.
/// Starts empty; entries are added explicitly via <see cref="Add"/>.
/// </summary>
public sealed class ProviderPool : IProviderPool
{
    private readonly List<ProviderEntry> _entries = [];

    public event EventHandler? Changed;
    public event EventHandler<ProviderEntry>? EntryAdded;

    public IReadOnlyList<ProviderEntry> All    => _entries;
    public ProviderEntry                Active => _entries.First(e => e.IsActive);

    public ProviderEntry Add(string name, IAssetProvider provider, string pluginId = "", Dictionary<string, string>? values = null)
    {
        var entry = new ProviderEntry
        {
            Name      = name,
            IsOpen    = true,
            IsEnabled = true,
            Provider  = provider,
            PluginId  = pluginId,
            Values    = values is null ? [] : new Dictionary<string, string>(values),
        };
        _entries.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        EntryAdded?.Invoke(this, entry);
        return entry;
    }

    public ProviderEntry RestoreEntry(
        string name, IAssetProvider provider, int position,
        bool isOpen, bool isEnabled, bool isActive,
        string pluginId = "", Dictionary<string, string>? values = null)
    {
        if (isActive)
            foreach (var e in _entries) e.IsActive = false;

        var entry = new ProviderEntry
        {
            Name      = name,
            Position  = position,
            IsOpen    = isOpen,
            IsEnabled = isEnabled,
            IsActive  = isActive,
            Provider  = provider,
            PluginId  = pluginId,
            Values    = values is null ? [] : new Dictionary<string, string>(values),
        };
        _entries.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        EntryAdded?.Invoke(this, entry);
        return entry;
    }

    public List<ProjectProviderEntry> ToPersistedEntries()
    {
        var result = new List<ProjectProviderEntry>(_entries.Count);
        for (var i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            result.Add(new ProjectProviderEntry
            {
                Position  = i,
                Name      = e.Name,
                PluginId  = e.PluginId,
                Values    = new Dictionary<string, string>(e.Values),
                IsOpen    = e.IsOpen,
                IsEnabled = e.IsEnabled,
                IsActive  = e.IsActive,
            });
        }
        return result;
    }

    public void SetActive(Guid id)
    {
        foreach (var e in _entries) e.IsActive = e.Id == id;
        var entry = Find(id);
        if (entry is null) return;
        entry.IsOpen    = true;
        entry.IsEnabled = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Open(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.IsOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Close(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.IsOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Enable(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.IsEnabled = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Disable(Guid id)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.IsEnabled = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenAll()
    {
        foreach (var e in _entries) e.IsOpen = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CloseAll()
    {
        foreach (var e in _entries) e.IsOpen = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EnableAll()
    {
        foreach (var e in _entries) e.IsEnabled = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void DisableAll()
    {
        foreach (var e in _entries) e.IsEnabled = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(Guid id, string name)
    {
        var entry = Find(id);
        if (entry is null) return;
        entry.Name = name;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        var entry = Find(id);
        if (entry is null || entry.IsActive) return;
        _entries.Remove(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAll()
    {
        _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private ProviderEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);
}
