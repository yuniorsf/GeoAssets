using System.Diagnostics.CodeAnalysis;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// A store-served <see cref="PostgresAssetProvider"/>'s features by id (XD01-449): at most <c>capacity</c> of them, the
/// least recently used dropped first; a capacity of zero keeps nothing. Thread-safe.
/// </summary>
internal sealed class FeatureByIdCache(int capacity)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<GeoFeature>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<GeoFeature> _recency = new();

    public bool TryGet(string id, [NotNullWhen(true)] out GeoFeature? feature)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(id, out var node))
            {
                _recency.Remove(node);
                _recency.AddFirst(node);
                feature = node.Value;
                return true;
            }
        }

        feature = null;
        return false;
    }

    public void Set(GeoFeature feature)
    {
        if (capacity == 0)
            return;

        lock (_gate)
        {
            if (_index.Remove(feature.Id, out var existing))
                _recency.Remove(existing);

            _index[feature.Id] = _recency.AddFirst(feature);
            while (_index.Count > capacity && _recency.Last is { } oldest)
            {
                _recency.RemoveLast();
                _index.Remove(oldest.Value.Id);
            }
        }
    }

    public void Remove(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (_index.Remove(id, out var node))
                    _recency.Remove(node);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _index.Clear();
            _recency.Clear();
        }
    }
}
