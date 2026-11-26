namespace GeoAssets.Core.Rendering;

/// <summary>
/// The named set of layers one tile URL carries (XD01-431): <c>/tiles/{Id}/…</c> serves, in each tile, one source layer
/// per entry of <see cref="LayerIds"/> (an MVT layer, or a raster drawing them in order). <see cref="Id"/> and every layer
/// id follow <see cref="TileKeySegment"/>, since they become URL and cache key segments; a layer id is also what
/// <see cref="ILodPolicy.Decide"/> is asked about.
/// </summary>
public sealed class TileLayerSet
{
    /// <summary>The set <paramref name="id"/> of <paramref name="layerIds"/>, in that order.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="layerIds"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">An id breaks <see cref="TileKeySegment"/>, there is no layer, or a layer appears twice (ordinal).</exception>
    public TileLayerSet(string id, IEnumerable<string> layerIds)
    {
        TileKeySegment.EnsureValid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(layerIds);

        var layers = layerIds.ToArray();
        if (layers.Length == 0)
            throw new ArgumentException("A tile layer set must name at least one layer.", nameof(layerIds));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            TileKeySegment.EnsureValid(layer, nameof(layerIds));
            if (!seen.Add(layer))
                throw new ArgumentException($"The layer '{layer}' appears more than once.", nameof(layerIds));
        }

        Id = id;
        LayerIds = layers;
    }

    /// <summary>The set's id — the <c>{layer}</c> segment of its tile URLs.</summary>
    public string Id { get; }

    /// <summary>The layers of each tile, bottom first; never empty, no duplicates.</summary>
    public IReadOnlyList<string> LayerIds { get; }
}
