namespace GeoAssets.Core.Rendering;

/// <summary>
/// Publish port for dirty tiles (ADR-002 D6, XD01-431): a writer of features (the PostGIS repository, the mutation-batch
/// applier) reports where its write changed what tiles show, and the implementation makes those tiles re-render — evicts
/// them from the <see cref="ITileCache"/> (<see cref="DirtyTileRegion.TileRanges"/> → <see cref="ITileCache.RemoveAsync"/>)
/// and tells whoever else holds them. Called only <b>after</b> the write's transaction commits — never inside it, so a
/// rolled-back write evicts nothing and no reader re-caches the old tile between the eviction and the commit. The write
/// stands whatever this does: a caller must not fail or undo a committed write because invalidation failed.
/// Implementations are idempotent and thread-safe.
/// </summary>
public interface ITileInvalidation
{
    /// <summary>Marks the tiles of every region in <paramref name="regions"/> dirty (one committed write's regions). An empty collection is a no-op.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="regions"/> is <c>null</c>.</exception>
    Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, CancellationToken ct);

    /// <summary>
    /// Marks the tiles of every region in <paramref name="regions"/> dirty for one committed write of
    /// <paramref name="organizationId"/>'s features (XD01-458): an implementation that versions tiles per scope need only
    /// change the versions of the scopes that can see that organization's features. <see cref="Guid.Empty"/> is the
    /// organization-less (shared) features, which every scope sees. The default treats it as
    /// <see cref="InvalidateAsync(IReadOnlyCollection{DirtyTileRegion}, CancellationToken)"/>: every scope's tiles.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="regions"/> is <c>null</c>.</exception>
    Task InvalidateAsync(IReadOnlyCollection<DirtyTileRegion> regions, Guid organizationId, CancellationToken ct) =>
        InvalidateAsync(regions, ct);
}
