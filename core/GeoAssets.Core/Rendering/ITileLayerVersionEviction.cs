namespace GeoAssets.Core.Rendering;

/// <summary>
/// An <see cref="ITileCache"/> that can also evict one layer version whole (XD01-464): every tile cached for
/// <c>(scope, layerSetId, layerVersion)</c>, whatever its address or format — e.g. the tiles of an offline map pack being
/// deleted, whose version is the pack's id. Kept apart from <see cref="ITileCache"/>, so a cache that can't do it cheaply
/// (a tag-based server cache) needn't.
/// </summary>
public interface ITileLayerVersionEviction
{
    /// <summary>
    /// Evicts every entry whose key starts with <see cref="TileCacheKey.LayerVersionPrefix"/> of the arguments.
    /// Idempotent: evicting what is not cached is a no-op.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="scope"/> is <c>default</c>, or <paramref name="layerSetId"/>/<paramref name="layerVersion"/> breaks <see cref="TileKeySegment"/>.</exception>
    Task RemoveLayerVersionAsync(TileCacheScope scope, string layerSetId, string layerVersion, CancellationToken ct);
}
