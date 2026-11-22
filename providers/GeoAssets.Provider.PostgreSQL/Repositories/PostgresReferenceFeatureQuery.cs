using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The PostGIS pushdown of <see cref="IReferenceFeatureQuery"/> (XD01-330): one <c>ST_Intersects</c> query against
/// <c>geo_entity.geom</c> that projects only the reference fields — id, name, asset type and geometry — with the type
/// filter, the scope semi-join on <c>geo_entity_attribute</c> (the indexed copy of the <c>scopeId</c> custom attribute),
/// the ordinal ordering and the cut all in SQL, then the asset types' names in a second read of the same transaction.
///
/// <para><b>Tenant scoping.</b> Like <see cref="PostgresAssetSpatialQuery"/>, every call opens its own short-lived
/// <see cref="GeoAssetsDbContext"/> — but for a tenant explicitly set to the organization asked for, for this call
/// only; the request's own <see cref="ITenantContext"/> is never consulted or changed, so the caller's other queries in
/// the same request keep their own scope. That tenant scopes the read three independent ways, none of them relaxed:
/// <list type="number">
///   <item>the context's global query filter (<c>OrganizationId = that organization</c>);</item>
///   <item>the transaction is stamped by <see cref="TenantTransactionInterceptor"/> with that organization
///   (<c>set_config('app.current_organization_id', …, is_local: true)</c>), what the <c>geo_entity</c> Row-Level
///   Security policy reads — transaction-local, so nothing carries over to the next user of a pooled connection;</item>
///   <item>an explicit <c>OrganizationId = @organizationId</c> predicate.</item>
/// </list>
/// Asset types are read for that organization or the shared catalog only.</para>
/// </summary>
public sealed class PostgresReferenceFeatureQuery : IReferenceFeatureQuery
{
    private const string OrdinalCollation = "C";

    /// <summary>The custom attribute <see cref="IScopeableResource.ScopeId"/> reads on a <see cref="GeoFeature"/>.</summary>
    private const string ScopeAttributeKey = "scopeId";

    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;

    /// <param name="dbOptions">
    /// Options for the short-lived contexts. Must not enable a retrying execution strategy: each call runs in an explicit
    /// transaction.
    /// </param>
    public PostgresReferenceFeatureQuery(DbContextOptions<GeoAssetsDbContext> dbOptions)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        _dbOptions = dbOptions;
    }

    public async Task<IReadOnlyList<ReferenceFeature>> GetInBoundsAsync(
        Guid organizationId, BoundingBox bbox, ReferenceFeatureFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);
        ct.ThrowIfCancellationRequested();

        string[]? scopes = filter.ScopeIds is null ? null : [.. filter.ScopeIds.Distinct(StringComparer.Ordinal)];
        if (scopes is { Length: 0 }) return [];

        var resourceOrganization = new ResourceOrganization(organizationId);
        var options = new DbContextOptionsBuilder<GeoAssetsDbContext>(_dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(resourceOrganization))
            .Options;
        var envelope = Envelope(bbox);

        var db = new GeoAssetsDbContext(options, resourceOrganization);
        await using (db.ConfigureAwait(false))
        {
            // The transaction exists for TenantTransactionInterceptor's transaction-local stamp, not for atomicity.
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var rows = db.GeoEntities
                    .AsNoTracking()
                    .Where(e => e.OrganizationId == organizationId && e.Geom != null && e.Geom.Intersects(envelope));

                if (!string.IsNullOrEmpty(filter.AssetTypeId))
                {
                    var assetTypeId = filter.AssetTypeId;
                    rows = rows.Where(e => e.AssetTypeId == assetTypeId);
                }

                if (scopes is not null)
                    rows = rows.Where(e => db.GeoEntityAttributes.Any(
                        a => a.EntityId == e.Id && a.Key == ScopeAttributeKey && scopes.Contains(a.Value)));

                var page = await rows
                    .OrderBy(e => EF.Functions.Collate(e.Id, OrdinalCollation))
                    .Take(filter.MaxResults)
                    .Select(e => new ReferenceRow(e.Id, e.Name, e.AssetTypeId, e.Geom!))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var typeNames = await AssetTypeNamesAsync(db, organizationId, page, ct).ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return
                [
                    .. page.Select(r => new ReferenceFeature(
                        r.Id,
                        GeoGeometry.FromNts(r.Geom),
                        r.Name,
                        r.AssetTypeId,
                        Guid.TryParse(r.AssetTypeId, out var typeId) ? typeNames.GetValueOrDefault(typeId, string.Empty) : string.Empty)),
                ];
            }
        }
    }

    private static async Task<Dictionary<Guid, string>> AssetTypeNamesAsync(
        GeoAssetsDbContext db, Guid organizationId, List<ReferenceRow> page, CancellationToken ct)
    {
        Guid[] typeIds = [.. page.Select(r => Guid.TryParse(r.AssetTypeId, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct()];
        if (typeIds.Length == 0) return [];

        var types = await db.AssetTypes
            .AsNoTracking()
            .Where(t => typeIds.Contains(t.Id) && (t.OrganizationId == organizationId || t.OrganizationId == Guid.Empty))
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return types.ToDictionary(t => t.Id, t => t.Name);
    }

    private static Geometry Envelope(BoundingBox bbox)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var envelope = factory.ToGeometry(new Envelope(bbox.MinLon, bbox.MaxLon, bbox.MinLat, bbox.MaxLat));
        envelope.SRID = 4326;
        return envelope;
    }

    private sealed class ResourceOrganization(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }

    private sealed record ReferenceRow(string Id, string Name, string AssetTypeId, Geometry Geom);
}
