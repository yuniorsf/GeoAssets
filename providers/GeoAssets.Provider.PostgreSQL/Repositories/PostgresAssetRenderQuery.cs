using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The PostGIS pushdown of <see cref="IAssetRenderQuery"/> (XD01-372): one <c>ST_Intersects</c> query per tile that
/// projects only what drawing needs — id, name, asset type, layer override, custom attributes (the layer-rule inputs) and
/// geometry; no topology. Tenant-scoped like <see cref="PostgresAssetSpatialQuery"/>: each read runs in a transaction
/// stamped by <see cref="TenantTransactionInterceptor"/> on a <see cref="GeoAssetsDbContext"/> built for the request's
/// <see cref="ITenantContext"/>, reading its organization's features plus the shared ones. Colors come from
/// <see cref="RenderStyle"/> over the request's catalog (<paramref name="styling"/>).
///
/// <para><b>Cancellation.</b> Map clients cancel tile requests freely, often while a connection is still being opened;
/// handing that token to Npgsql can abort a connection mid-handshake and spoil its pool slot. A request already cancelled
/// returns nothing without touching the database; otherwise the query runs under its own <see cref="QueryTimeout"/>.</para>
/// </summary>
public sealed class PostgresAssetRenderQuery : IAssetRenderQuery
{
    /// <summary>The longest a render query may run.</summary>
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions AttributeJson = new(JsonSerializerDefaults.Web);

    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ITenantContext _tenantContext;
    private readonly IAssetProvider _styling;
    private readonly TimeProvider _timeProvider;

    public PostgresAssetRenderQuery(
        DbContextOptions<GeoAssetsDbContext> dbOptions, ITenantContext? tenantContext, IAssetProvider styling, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(styling);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _tenantContext = tenantContext ?? NullTenantContext.Instance;
        _dbOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>(dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(_tenantContext))
            .Options;
        _styling = styling;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (ct.IsCancellationRequested) return [];

        var envelope = Envelope(bbox);
        var rows = await ReadAsync(
            async (db, token) =>
            {
                var query = Visible(db).AsNoTracking().Where(e => e.Geom != null && e.Geom.Intersects(envelope));
                if (!string.IsNullOrEmpty(options.AssetTypeId))
                    query = query.Where(e => e.AssetTypeId == options.AssetTypeId);

                return await query
                    .Select(e => new RenderInputs(e.Id, e.Name, e.AssetTypeId, e.LayerId, e.CustomAttributesJson, e.Geom!))
                    .ToListAsync(token)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        var style = RenderStyle.Of(_styling);
        return [.. rows.Select(r => Renderable(r, style))];
    }

    public async Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || maxCount <= 0) return [];

        var envelope = Envelope(bbox);
        return await ReadAsync(
            async (db, token) => (IReadOnlyList<FeatureInfo>)await Visible(db)
                .AsNoTracking()
                .Where(e => e.Geom != null && e.Geom.Intersects(envelope))
                .OrderBy(e => EF.Functions.Collate(e.Id, "C"))
                .Take(maxCount)
                .Select(e => new FeatureInfo(e.Id, e.Name, e.AssetTypeId, e.Description))
                .ToListAsync(token)
                .ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task<T> ReadAsync<T>(Func<GeoAssetsDbContext, CancellationToken, Task<T>> read)
    {
        using var timeout = new CancellationTokenSource(QueryTimeout, _timeProvider);
        var db = new GeoAssetsDbContext(_dbOptions, _tenantContext);
        await using (db.ConfigureAwait(false))
        {
            // The transaction exists for TenantTransactionInterceptor's transaction-local stamp; this is a single read.
            var transaction = await db.Database.BeginTransactionAsync(timeout.Token).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var result = await read(db, timeout.Token).ConfigureAwait(false);
                await transaction.CommitAsync(timeout.Token).ConfigureAwait(false);
                return result;
            }
        }
    }

    /// <summary>
    /// The rows the caller sees: its organization's plus the shared (<see cref="Guid.Empty"/>) ones — the context's own
    /// query filter matches the tenant's organization alone, so it is replaced by that union, as an organization's
    /// <see cref="PostgresAssetProvider"/> does; without an organization, the filter's shared rows.
    /// </summary>
    private IQueryable<Entities.GeoEntityRow> Visible(GeoAssetsDbContext db)
    {
        var organizationId = _tenantContext.CurrentOrganizationId;
        return organizationId == Guid.Empty
            ? db.GeoEntities
            : db.GeoEntities.IgnoreQueryFilters().Where(e => e.OrganizationId == organizationId || e.OrganizationId == Guid.Empty);
    }

    private static RenderableFeature Renderable(RenderInputs row, RenderStyle style)
    {
        var feature = new GeoFeature
        {
            Id = row.Id,
            Properties = new GeoFeatureProperties
            {
                Name = row.Name,
                AssetTypeId = row.AssetTypeId,
                LayerId = row.LayerId?.ToString() ?? string.Empty,
                CustomAttributes = JsonSerializer.Deserialize<Dictionary<string, string>>(row.CustomAttributesJson, AttributeJson) ?? [],
            },
        };

        return new RenderableFeature(row.Id, row.Name, row.AssetTypeId, GeoGeometry.FromNts(row.Geom), style.ColorOf(feature));
    }

    private static Geometry Envelope(BoundingBox bbox)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var envelope = factory.ToGeometry(new Envelope(bbox.MinLon, bbox.MaxLon, bbox.MinLat, bbox.MaxLat));
        envelope.SRID = 4326;
        return envelope;
    }

    private sealed record RenderInputs(
        string Id, string Name, string AssetTypeId, Guid? LayerId, string CustomAttributesJson, Geometry Geom);
}
