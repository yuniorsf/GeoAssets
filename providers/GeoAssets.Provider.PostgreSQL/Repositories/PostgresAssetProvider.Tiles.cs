using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Entities;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// Dirty-tile invalidation of this provider's feature writes (ADR-002 D6, XD01-432). Built with an
/// <see cref="ITileInvalidation"/>, each write that changes features (<see cref="Add"/>, <see cref="Update"/>,
/// <see cref="AddRange"/>, <see cref="Delete"/>, <see cref="Clear"/>, <see cref="LoadAll"/>) hands it the boxes the
/// written features had before and have after the write (<see cref="DirtyTileRegions"/>) — once the write has
/// committed, and only then: a write that fails publishes nothing. Inside a transaction its caller owns (as
/// <see cref="PostgresAssetMutationBatchApplier"/>'s), the regions are kept instead, and the caller publishes them with
/// <see cref="PublishDeferredDirtyTilesAsync"/> after its commit — or never, if it rolls back. A synchronous write
/// does not wait for the invalidation, which never fails it (<see cref="AfterCommitTileInvalidation"/>). Built without
/// one, nothing is tracked: no extra query, as before.
/// </summary>
public sealed partial class PostgresAssetProvider
{
    private readonly AfterCommitTileInvalidation? _tiles;
    private readonly List<BoundingBox?> _deferredDirtyBounds = [];

    /// <summary>
    /// Publishes the regions this provider's writes kept while inside its caller's transaction — call right after that
    /// transaction commits; nothing when there are none or no invalidation. Never throws.
    /// </summary>
    internal Task PublishDeferredDirtyTilesAsync(string writer)
    {
        if (_tiles is null || _deferredDirtyBounds.Count == 0) return Task.CompletedTask;

        var regions = DirtyTileRegions.Of(_deferredDirtyBounds);
        _deferredDirtyBounds.Clear();
        return _tiles.PublishAsync(regions, writer);
    }

    /// <summary>The stored boxes of those of <paramref name="ids"/> this provider may write (the context's query filter); none when nothing is tracked.</summary>
    private List<BoundingBox?> StoredBounds(List<string> ids)
    {
        if (_tiles is null || ids.Count == 0) return [];

        return [.. _db.GeoEntities
            .Where(e => ids.Contains(e.Id) && e.Geom != null)
            .Select(e => e.Geom!.Envelope)
            .AsEnumerable()
            .Select(envelope => DirtyTileRegions.BoundsOf(envelope.EnvelopeInternal))];
    }

    private static BoundingBox? BoundsOf(GeoFeature feature) =>
        DirtyTileRegions.BoundsOf(feature.Geometry?.NtsGeometry.EnvelopeInternal);

    private static BoundingBox? BoundsOf(GeoEntityRow row) =>
        DirtyTileRegions.BoundsOf(row.Geom?.EnvelopeInternal);

    /// <summary>
    /// After a committed write: hands the regions of <paramref name="bounds"/> to the invalidation, without waiting —
    /// or keeps them while the context is inside its caller's transaction. Nothing when nothing is tracked.
    /// </summary>
    private void PublishDirtyTiles(IEnumerable<BoundingBox?> bounds)
    {
        if (_tiles is null) return;

        if (_db.Database.CurrentTransaction is not null)
        {
            _deferredDirtyBounds.AddRange(bounds);
            return;
        }

        _ = _tiles.PublishAsync(DirtyTileRegions.Of(bounds), AfterCommitTileInvalidation.ProviderWriter);
    }
}
