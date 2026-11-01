using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

public sealed partial class PostgresAssetProvider
{
    /// <summary>
    /// Overrides <see cref="IAssetProvider"/>'s default (in-memory Haversine) implementation with
    /// a genuine server-side PostGIS <c>ST_DWithin</c> on a <c>geography</c> cast (XD01-221) —
    /// full spheroidal accuracy, and the computation never has to bring the row's geometry back
    /// to this process. Assumes the stored geometry's embedded SRID is 4326 (lon/lat), the same
    /// convention every other geometry predicate in this class already assumes without an
    /// explicit <c>ST_Transform</c> (e.g. <see cref="GetInBoundsAsync"/>'s <c>Intersects</c>).
    /// </summary>
    public Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(point);

        return _db.Database.SqlQuery<bool>($"""
            SELECT ST_DWithin(
                geom::geography,
                ST_SetSRID(ST_MakePoint({point.Longitude}, {point.Latitude}), 4326)::geography,
                {toleranceMeters}
            ) AS "Value"
            FROM geo_entity
            WHERE "Id" = {featureId} AND geom IS NOT NULL
            """)
            .SingleOrDefaultAsync();
    }
}
