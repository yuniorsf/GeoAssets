namespace GeoAssets.Core.Rendering;

/// <summary>
/// Storage port for rendered tiles (ADR-002 D6, XD01-431) — a server-side HybridCache/OutputCache adapter, an offline
/// SQLite pack. Keyed by <see cref="TileCacheKey"/>, which carries the caller's org and grant scope and the layer version,
/// so an entry is only ever served to a caller of the same scope. Implementations are thread-safe.
/// </summary>
public interface ITileCache
{
    /// <summary>The tile cached under <paramref name="key"/>, or <c>null</c> when there is none.</summary>
    Task<TilePayload?> GetAsync(TileCacheKey key, CancellationToken ct);

    /// <summary>Caches <paramref name="payload"/> under <paramref name="key"/>, replacing any entry already there.</summary>
    Task SetAsync(TileCacheKey key, TilePayload payload, CancellationToken ct);

    /// <summary>
    /// Evicts every entry of every tile in <paramref name="range"/> — all scopes, layer sets, versions and formats
    /// (by <see cref="TileCacheKey.AddressTagFor"/>, for a tag-based cache). Idempotent: evicting what is not cached is a no-op.
    /// </summary>
    Task RemoveAsync(TileRange range, CancellationToken ct);
}
