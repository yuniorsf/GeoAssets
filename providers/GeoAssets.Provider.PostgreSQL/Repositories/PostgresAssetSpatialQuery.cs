using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Persistence.Postgres.Interceptors;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// PostGIS pushdown implementation of <see cref="IAssetSpatialQuery"/>: the area test runs as
/// <c>ST_Covers</c> against <c>geo_entity.geom</c> (GiST-indexed), the attribute lookup as an
/// equality semi-join on <c>geo_entity_attribute</c> (the normalized, indexed copy of
/// <c>custom_attributes</c>), and the type filter, ordering and <see cref="AssetSpatialFilter.MaxResults"/>
/// cut all happen in SQL. Nothing is read from <see cref="PostgresAssetProvider"/>'s in-memory cache.
///
/// <para><b>Tenant scoping.</b> Unlike <see cref="PostgresAssetProvider"/> (a singleton whose
/// contexts are built without a tenant), every call here opens its own short-lived
/// <see cref="GeoAssetsDbContext"/> for the caller's <see cref="ITenantContext"/>, which scopes the
/// query three independent ways:
/// <list type="number">
///   <item>the context's global query filter (<c>OrganizationId = current organization</c>);</item>
///   <item>the query runs inside a transaction stamped by <see cref="TenantTransactionInterceptor"/>
///   (<c>set_config('app.current_organization_id', …, is_local: true)</c>), which is what the
///   <c>geo_entity</c> Row-Level Security policy reads — transaction-local, so nothing leaks to
///   the next user of a pooled connection;</item>
///   <item>an explicit <c>OrganizationId = @organizationId</c> predicate whenever
///   <see cref="AssetSpatialFilter.OrganizationId"/> is set.</item>
/// </list>
/// The result is therefore the intersection: a filter naming another organization than the
/// caller's yields an empty list, and an unset filter still returns only the caller's rows.</para>
///
/// <para><b>Ordering.</b> <c>ORDER BY "Id" COLLATE "C"</c> — byte order, matching the ordinal
/// ordering the port documents regardless of the database's default collation.</para>
/// </summary>
public sealed class PostgresAssetSpatialQuery : IAssetSpatialQuery
{
    private const string OrdinalCollation = "C";

    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ITenantContext _tenantContext;

    /// <param name="dbOptions">
    /// Options for the short-lived contexts. Must not enable a retrying execution strategy: each
    /// call runs in an explicit transaction.
    /// </param>
    /// <param name="tenantContext">The caller's tenant; <c>null</c> falls back to <see cref="NullTenantContext"/>.</param>
    public PostgresAssetSpatialQuery(DbContextOptions<GeoAssetsDbContext> dbOptions, ITenantContext? tenantContext = null)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);

        _tenantContext = tenantContext ?? NullTenantContext.Instance;
        _dbOptions = new DbContextOptionsBuilder<GeoAssetsDbContext>(dbOptions)
            .AddInterceptors(new TenantTransactionInterceptor(_tenantContext))
            .Options;
    }

    public Task<IReadOnlyList<GeoFeature>> FindCoveredByAsync(
        GeoPolygon area, AssetSpatialFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);

        var polygon = area.NtsGeometry;
        return QueryAsync(rows => rows.Where(e => e.Geom != null && polygon.Covers(e.Geom)), filter, ct);
    }

    public Task<IReadOnlyList<GeoFeature>> FindByAttributeValuesAsync(
        string attributeKey, IReadOnlyCollection<string> values, AssetSpatialFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attributeKey);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(filter.MaxResults);
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeKey);

        if (ct.IsCancellationRequested)
            return Task.FromCanceled<IReadOnlyList<GeoFeature>>(ct);

        if (values.Count == 0)
            return Task.FromResult<IReadOnlyList<GeoFeature>>([]);

        string[] wanted = [.. values.Distinct(StringComparer.Ordinal)];

        return string.Equals(attributeKey, AssetSpatialQueryKeys.FeatureId, StringComparison.Ordinal)
            ? QueryAsync(rows => rows.Where(e => wanted.Contains(e.Id)), filter, ct)
            : QueryAsync(
                (rows, db) => rows.Where(e => db.GeoEntityAttributes.Any(
                    a => a.EntityId == e.Id && a.Key == attributeKey && wanted.Contains(a.Value))),
                filter,
                ct);
    }

    private Task<IReadOnlyList<GeoFeature>> QueryAsync(
        Func<IQueryable<GeoEntityRow>, IQueryable<GeoEntityRow>> match, AssetSpatialFilter filter, CancellationToken ct) =>
        QueryAsync((rows, _) => match(rows), filter, ct);

    private async Task<IReadOnlyList<GeoFeature>> QueryAsync(
        Func<IQueryable<GeoEntityRow>, GeoAssetsDbContext, IQueryable<GeoEntityRow>> match,
        AssetSpatialFilter filter,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var db = new GeoAssetsDbContext(_dbOptions, _tenantContext);
        await using (db.ConfigureAwait(false))
        {
            // The transaction exists for TenantTransactionInterceptor's transaction-local stamp,
            // not for atomicity — this is a single read.
            var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var rows = match(db.GeoEntities.AsNoTracking(), db);

                if (filter.OrganizationId is { } organizationId)
                    rows = rows.Where(e => e.OrganizationId == organizationId);

                if (filter.AssetTypeIds is { Count: > 0 } assetTypeIds)
                {
                    string[] typeIds = [.. assetTypeIds];
                    rows = rows.Where(e => typeIds.Contains(e.AssetTypeId));
                }

                var page = await rows
                    .OrderBy(e => EF.Functions.Collate(e.Id, OrdinalCollation))
                    .Take(filter.MaxResults)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return [.. page.Select(PostgresAssetProvider.MapToFeature)];
            }
        }
    }
}
