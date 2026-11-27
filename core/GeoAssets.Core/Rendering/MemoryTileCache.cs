namespace GeoAssets.Core.Rendering;

/// <summary>
/// An in-process, size-bounded <see cref="ITileCache"/> (ADR-002 D6, XD01-434): entries live in memory, keyed by
/// <see cref="TileCacheKey.Value"/>, until the cache holds more than <see cref="MaxBytes"/> — then the least recently
/// used go first. Each entry is also indexed by its tile, so <see cref="RemoveAsync"/> evicts every scope, layer set,
/// version and format of the tiles in a range at a cost bounded by the smaller of the range and what is cached at its
/// zoom — never a walk of a range's <c>4^z</c> tiles. Thread-safe: one lock guards every operation, none of which does
/// I/O. Nothing is shared across processes.
/// </summary>
public sealed class MemoryTileCache : ITileCache
{
    /// <summary>The default <see cref="MaxBytes"/>: 256 MiB.</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;

    /// <summary>What an entry costs beyond its content: the key, the indexes and the payload object.</summary>
    internal const int EntryOverheadBytes = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _recency = new();
    private readonly Dictionary<TileAddress, HashSet<string>> _keysByTile = [];
    private readonly Dictionary<int, HashSet<TileAddress>> _tilesByZoom = [];
    private long _bytes;

    /// <summary>A cache holding at most <paramref name="maxBytes"/> (content plus <see cref="EntryOverheadBytes"/> per entry).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public MemoryTileCache(long maxBytes = DefaultMaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        MaxBytes = maxBytes;
    }

    /// <summary>The most the cache holds, in bytes; an entry larger than this alone is not kept.</summary>
    public long MaxBytes { get; }

    /// <summary>The bytes the cache holds now.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate) return _bytes;
        }
    }

    /// <summary>The entries the cache holds now.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <inheritdoc />
    public Task<TilePayload?> GetAsync(TileCacheKey key, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key.Value, out var node))
                return Task.FromResult<TilePayload?>(null);

            _recency.Remove(node);
            _recency.AddFirst(node);
            return Task.FromResult<TilePayload?>(node.Value.Payload);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <c>null</c>.</exception>
    public Task SetAsync(TileCacheKey key, TilePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var size = payload.Content.Length + (long)EntryOverheadBytes;

        lock (_gate)
        {
            if (_entries.TryGetValue(key.Value, out var existing))
                Remove(existing);

            if (size > MaxBytes)
                return Task.CompletedTask;

            var node = _recency.AddFirst(new Entry(key.Value, key.Address, payload, size));
            _entries.Add(key.Value, node);
            Index(key.Value, key.Address);
            _bytes += size;

            while (_bytes > MaxBytes && _recency.Last is { } oldest)
                Remove(oldest);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(TileRange range, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_tilesByZoom.TryGetValue(range.Zoom, out var cachedAtZoom))
                return Task.CompletedTask;

            // Whichever is smaller: the range's tiles or the tiles cached at its zoom.
            var dirty = range.Count <= cachedAtZoom.Count
                ? range.EnumerateTiles().Where(cachedAtZoom.Contains).ToList()
                : cachedAtZoom.Where(range.Contains).ToList();

            foreach (var tile in dirty)
            {
                foreach (var key in _keysByTile[tile].ToList())
                    Remove(_entries[key]);
            }
        }

        return Task.CompletedTask;
    }

    private void Index(string key, TileAddress address)
    {
        if (!_keysByTile.TryGetValue(address, out var keys))
        {
            keys = new HashSet<string>(StringComparer.Ordinal);
            _keysByTile.Add(address, keys);

            if (!_tilesByZoom.TryGetValue(address.Z, out var tiles))
            {
                tiles = [];
                _tilesByZoom.Add(address.Z, tiles);
            }

            tiles.Add(address);
        }

        keys.Add(key);
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        var entry = node.Value;
        _recency.Remove(node);
        _entries.Remove(entry.Key);
        _bytes -= entry.Size;

        var keys = _keysByTile[entry.Address];
        keys.Remove(entry.Key);
        if (keys.Count > 0) return;

        _keysByTile.Remove(entry.Address);
        var tiles = _tilesByZoom[entry.Address.Z];
        tiles.Remove(entry.Address);
        if (tiles.Count == 0) _tilesByZoom.Remove(entry.Address.Z);
    }

    private sealed record Entry(string Key, TileAddress Address, TilePayload Payload, long Size);
}
