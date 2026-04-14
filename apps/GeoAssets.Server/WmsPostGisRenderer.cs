using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoAssets.Server;

/// <summary>
/// Minimal-projection PostGIS data access for the WMS renderer.
///
/// Each method opens its own short-lived <see cref="GeoAssetsDbContext"/> via
/// <see cref="IDbContextFactory{TContext}"/> so concurrent tile requests are
/// always safe (no shared DbContext state).
///
/// The projected SQL selects <b>only</b> the columns the renderer needs
/// (geometry, color, name, assetTypeId) — no JSONB topology or custom-attribute
/// columns are loaded, making each tile query significantly leaner than going
/// through <see cref="GeoAssets.Core.Interfaces.IAssetProvider"/>.
///
/// <b>Cancellation policy:</b> Leaflet fires dozens of tile requests in parallel
/// and cancels the ones that scroll out of view almost immediately — often while
/// Npgsql is still opening a connection from the pool.  Passing the HTTP request
/// <see cref="CancellationToken"/> directly to Npgsql causes
/// <c>ConnectAsync</c> to abort mid-handshake, which corrupts the pool slot and
/// throws <see cref="TaskCanceledException"/> in user code.
///
/// To avoid this, all DB operations use an <i>independent</i>
/// <see cref="CancellationTokenSource"/> with a fixed <see cref="QueryTimeout"/>
/// rather than the HTTP token.  If the query still hasn't finished after the
/// timeout, it is cancelled cleanly.  The caller can still detect client-side
/// cancellation via the original HTTP token and return an empty result early.
/// </summary>
public sealed class WmsPostGisRenderer(IDbContextFactory<GeoAssetsDbContext> factory)
{
    /// <summary>Per-query timeout for PostGIS operations (tile rendering must be fast).</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    // ── Render row ────────────────────────────────────────────────────────────

    /// <summary>Minimal data required to draw one feature on a tile.</summary>
    public sealed class RenderRow
    {
        public required Geometry Geom        { get; init; }
        public required string   Color       { get; init; }
        public required string   Name        { get; init; }
        public required string   AssetTypeId { get; init; }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all features whose geometry intersects the given bounding box.
    /// The WHERE clause translates to <c>ST_Intersects(geom, ST_MakeEnvelope(...))</c>
    /// in PostgreSQL — only matching rows travel over the wire.
    ///
    /// Optionally filtered to a single <paramref name="assetTypeId"/> when the WMS
    /// LAYERS parameter encodes a specific type (e.g. <c>geoassets:feature:{typeId}</c>).
    ///
    /// Returns an empty list immediately when <paramref name="httpCt"/> is already
    /// cancelled (client navigated away) without touching the DB.
    /// </summary>
    public async Task<List<RenderRow>> GetInBoundsAsync(
        double  minLon,    double minLat, double maxLon, double maxLat,
        string? assetTypeId  = null,
        CancellationToken httpCt = default)
    {
        // Fast-exit: client already cancelled — no point opening a connection.
        if (httpCt.IsCancellationRequested) return [];

        var bbox = MakeBbox(minLon, minLat, maxLon, maxLat);

        using var cts = new CancellationTokenSource(QueryTimeout);
        await using var db = await factory.CreateDbContextAsync(cts.Token);

        var query = db.GeoEntities
            .AsNoTracking()
            .Include(e => e.AssetType)
            .Where(e => e.Geom != null && e.Geom.Intersects(bbox));

        if (!string.IsNullOrEmpty(assetTypeId))
            query = query.Where(e => e.AssetTypeId == assetTypeId);

        return await query
            .Select(e => new RenderRow
            {
                Geom        = e.Geom!,
                Color       = e.AssetType != null ? e.AssetType.Color : "#3388ff",
                Name        = e.Name,
                AssetTypeId = e.AssetTypeId
            })
            .ToListAsync(cts.Token);
    }

    /// <summary>
    /// Returns features near a clicked pixel for GetFeatureInfo.
    /// The tolerance bbox is ~1 % of the tile size (caller-computed).
    /// </summary>
    public async Task<List<FeatureInfoRow>> GetFeatureInfoAsync(
        double clickLon,     double clickLat,
        double toleranceLon, double toleranceLat,
        int    maxCount = 1,
        CancellationToken httpCt = default)
    {
        if (httpCt.IsCancellationRequested) return [];

        var bbox = MakeBbox(
            clickLon - toleranceLon, clickLat - toleranceLat,
            clickLon + toleranceLon, clickLat + toleranceLat);

        using var cts = new CancellationTokenSource(QueryTimeout);
        await using var db = await factory.CreateDbContextAsync(cts.Token);

        return await db.GeoEntities
            .AsNoTracking()
            .Where(e => e.Geom != null && e.Geom.Intersects(bbox))
            .Take(maxCount)
            .Select(e => new FeatureInfoRow
            {
                Id          = e.Id,
                Name        = e.Name,
                AssetTypeId = e.AssetTypeId,
                Description = e.Description
            })
            .ToListAsync(cts.Token);
    }

    /// <summary>Returns all asset types stored in the DB for GetCapabilities.</summary>
    public async Task<List<AssetTypeInfo>> GetAssetTypesAsync(CancellationToken httpCt = default)
    {
        if (httpCt.IsCancellationRequested) return [];

        using var cts = new CancellationTokenSource(QueryTimeout);
        await using var db = await factory.CreateDbContextAsync(cts.Token);

        return await db.AssetTypes
            .AsNoTracking()
            .Select(t => new AssetTypeInfo { Id = t.Id, Name = t.Name, Color = t.Color })
            .ToListAsync(cts.Token);
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public sealed class FeatureInfoRow
    {
        public required string Id          { get; init; }
        public required string Name        { get; init; }
        public required string AssetTypeId { get; init; }
        public required string Description { get; init; }
    }

    public sealed class AssetTypeInfo
    {
        public required Guid   Id    { get; init; }
        public required string Name  { get; init; }
        public required string Color { get; init; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Geometry MakeBbox(double minLon, double minLat, double maxLon, double maxLat)
    {
        var f    = new GeometryFactory(new PrecisionModel(), 4326);
        var bbox = f.ToGeometry(new Envelope(minLon, maxLon, minLat, maxLat));
        bbox.SRID = 4326;
        return bbox;
    }
}
