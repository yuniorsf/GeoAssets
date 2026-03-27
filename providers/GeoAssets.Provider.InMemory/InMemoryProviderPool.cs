using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IProviderPool"/>.
/// Creates one default active entry on construction; additional entries
/// each own an independent <see cref="InMemoryAssetProvider"/>.
/// </summary>
public sealed class InMemoryRepositoryPool : IProviderPool
{
    private readonly List<ProviderEntry> _entries = [];

    public event EventHandler? Changed;

    public InMemoryRepositoryPool()
    {
        _entries.Add(new ProviderEntry
        {
            Name       = "Default",
            IsOpen     = true,
            IsEnabled  = true,
            IsActive   = true,
            Provider = new InMemoryAssetProvider()
        });
    }

    public IReadOnlyList<ProviderEntry> All    => _entries;
    public ProviderEntry                Active => _entries.First(e => e.IsActive);

    public ProviderEntry Add(string name)
    {
        var entry = new ProviderEntry
        {
            Name       = name,
            IsOpen     = true,
            IsEnabled  = true,
            Provider = new InMemoryAssetProvider()
        };
        _entries.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public ProviderEntry AddExternal(string name, IAssetProvider provider)
    {
        var entry = new ProviderEntry
        {
            Name       = name,
            IsOpen     = true,
            IsEnabled  = true,
            Provider = provider
        };
        _entries.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
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

    private ProviderEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);
}
