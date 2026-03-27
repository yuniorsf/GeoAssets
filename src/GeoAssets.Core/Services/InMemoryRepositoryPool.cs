using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Core.Services;

/// <summary>
/// In-memory implementation of <see cref="IRepositoryPool"/>.
/// Creates one default active entry on construction; additional entries
/// each own an independent <see cref="InMemoryAssetRepository"/>.
/// </summary>
public sealed class InMemoryRepositoryPool : IRepositoryPool
{
    private readonly List<RepositoryEntry> _entries = [];

    public event EventHandler? Changed;

    public InMemoryRepositoryPool()
    {
        _entries.Add(new RepositoryEntry
        {
            Name       = "Default",
            IsOpen     = true,
            IsEnabled  = true,
            IsActive   = true,
            Repository = new InMemoryAssetRepository()
        });
    }

    public IReadOnlyList<RepositoryEntry> All    => _entries;
    public RepositoryEntry                Active => _entries.First(e => e.IsActive);

    public RepositoryEntry Add(string name)
    {
        var entry = new RepositoryEntry
        {
            Name       = name,
            IsOpen     = true,
            IsEnabled  = true,
            Repository = new InMemoryAssetRepository()
        };
        _entries.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public RepositoryEntry AddExternal(string name, IAssetRepository repository)
    {
        var entry = new RepositoryEntry
        {
            Name       = name,
            IsOpen     = true,
            IsEnabled  = true,
            Repository = repository
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

    private RepositoryEntry? Find(Guid id) => _entries.FirstOrDefault(e => e.Id == id);
}
