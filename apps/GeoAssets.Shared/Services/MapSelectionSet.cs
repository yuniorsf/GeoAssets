using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Default <see cref="IMapSelectionSet"/> (XD01-310). Not thread-safe: like the other panel-state
/// services it is mutated only from the renderer's synchronization context.
/// </summary>
public sealed class MapSelectionSet : IMapSelectionSet
{
    /// <summary>Default <see cref="MaxCount"/> — the same bound the server puts on one resolved gesture.</summary>
    public const int DefaultMaxCount = 10_000;

    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public MapSelectionSet()
        : this(DefaultMaxCount)
    {
    }

    public MapSelectionSet(int maxCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        MaxCount = maxCount;
    }

    public IReadOnlySet<string> Ids => _ids;

    public int MaxCount { get; }

    public event Action<MapSelectionChange>? Changed;

    public MapSelectionChange Add(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var added = new List<string>();
        var overflow = 0;
        foreach (var id in Normalize(ids))
        {
            if (_ids.Contains(id)) continue;
            if (_ids.Count >= MaxCount)
            {
                overflow++;
                continue;
            }

            _ids.Add(id);
            added.Add(id);
        }

        return Publish(new MapSelectionChange(added, [], overflow));
    }

    public MapSelectionChange Remove(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var removed = Normalize(ids).Where(_ids.Remove).ToList();
        return Publish(new MapSelectionChange([], removed, 0));
    }

    public MapSelectionChange Toggle(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (string.IsNullOrWhiteSpace(id)) return MapSelectionChange.None;

        return _ids.Contains(id) ? Remove([id]) : Add([id]);
    }

    public MapSelectionChange Replace(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var wanted = Normalize(ids).ToList();
        var overflow = Math.Max(0, wanted.Count - MaxCount);
        var kept = new HashSet<string>(wanted.Take(MaxCount), StringComparer.Ordinal);

        var removed = _ids.Where(id => !kept.Contains(id)).ToList();
        var added = wanted.Take(MaxCount).Where(id => !_ids.Contains(id)).ToList();

        _ids.ExceptWith(removed);
        _ids.UnionWith(added);

        return Publish(new MapSelectionChange(added, removed, overflow));
    }

    public MapSelectionChange Clear()
    {
        var removed = _ids.ToList();
        _ids.Clear();
        return Publish(new MapSelectionChange([], removed, 0));
    }

    /// <summary>Non-blank ids, first occurrence of each, in the order given.</summary>
    private static IEnumerable<string> Normalize(IReadOnlyCollection<string> ids) =>
        ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal);

    private MapSelectionChange Publish(MapSelectionChange change)
    {
        if (change.IsEmpty) return MapSelectionChange.None;

        Changed?.Invoke(change);
        return change;
    }
}
