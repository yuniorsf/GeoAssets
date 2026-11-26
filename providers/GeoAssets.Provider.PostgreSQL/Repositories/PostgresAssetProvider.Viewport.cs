using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>The viewport read of <c>GET /features/bounds</c> (XD01-430).</summary>
public sealed partial class PostgresAssetProvider
{
    /// <summary>
    /// <see cref="IAssetProvider.GetInViewportAsync"/> in SQL (XD01-430): the zoom's hidden asset types
    /// (<see cref="ZoomRangeQueries"/>), <c>ST_Intersects</c>, <c>ORDER BY "Id" COLLATE "C"</c> (ordinal, as the default's
    /// sort) and <c>LIMIT</c> <paramref name="maxCount"/> run in PostgreSQL over this provider's rows (an organization's
    /// own and the shared ones), so only the returned rows are read and mapped. Like
    /// <see cref="PostgresAssetRenderQuery"/>, a request already cancelled never reaches the database, and a started read
    /// is not handed the token: map clients abandon viewports freely, and an abort mid-handshake can spoil a pooled
    /// connection.
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> GetInViewportAsync(ViewportQuery viewport, int maxCount, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (maxCount <= 0) return [];

        var bbox = Envelope(viewport.Bounds);
        var db = NewContext();
        try
        {
            var hidden = await ZoomRangeQueries.HiddenAssetTypeIdsAsync(db, viewport.Zoom, CancellationToken.None).ConfigureAwait(false);
            var rows = await QueryableRows(db)
                .AsNoTracking()
                .WithoutAssetTypes(hidden)
                .Where(e => e.Geom != null && e.Geom.Intersects(bbox))
                .OrderBy(e => EF.Functions.Collate(e.Id, "C"))
                .Take(maxCount)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);

            return rows.ConvertAll(MapToFeature);
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Geometry Envelope(BoundingBox bounds)
    {
        var factory  = new GeometryFactory(new PrecisionModel(), 4326);
        var envelope = factory.ToGeometry(new Envelope(bounds.MinLon, bounds.MaxLon, bounds.MinLat, bounds.MaxLat));
        envelope.SRID = 4326;
        return envelope;
    }
}
