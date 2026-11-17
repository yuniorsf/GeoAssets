using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The organization half of an <see cref="OrganizationAssetCatalog"/> (XD01-338).
///
/// <para>Built for one organization, the provider opens every context — the long-lived one its
/// writes use and the short-lived ones its reads use — for that organization, so its in-memory
/// cache holds that organization's own features only (the context's query filter). The reads it
/// pushes down to PostgreSQL (<see cref="GetInBoundsAsync"/>, <see cref="GetPageAsync"/>) return
/// that organization's features plus the organization-less (shared) ones in one query, so their
/// paging and counting stay in SQL; <see cref="OrganizationAssetCatalog"/> adds the shared
/// features to the cache-backed reads from the shared catalog.</para>
///
/// <para>Built without an organization (the public constructor), nothing changes: contexts are
/// opened without a tenant, as before.</para>
/// </summary>
public sealed partial class PostgresAssetProvider
{
    private readonly ITenantContext? _organization;

    /// <summary>A provider for <paramref name="organizationId"/>'s features — see the type's remarks.</summary>
    /// <exception cref="ArgumentException"><paramref name="organizationId"/> is <see cref="Guid.Empty"/>.</exception>
    internal PostgresAssetProvider(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        Guid organizationId,
        ILogger<PostgresAssetProvider> logger,
        TimeProvider timeProvider)
        : this(dbOptions, OrganizationOf(organizationId), logger, timeProvider)
    {
    }

    private PostgresAssetProvider(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        ITenantContext organization,
        ILogger<PostgresAssetProvider> logger,
        TimeProvider timeProvider)
        : this(new GeoAssetsDbContext(dbOptions, organization), dbOptions, logger, timeProvider)
    {
        _organization = organization;
    }

    private static Organization OrganizationOf(Guid organizationId) =>
        organizationId == Guid.Empty
            ? throw new ArgumentException("An organization's provider needs an organization.", nameof(organizationId))
            : new Organization(organizationId);

    /// <summary>A short-lived context for this provider's organization, or without a tenant.</summary>
    private GeoAssetsDbContext NewContext() =>
        _organization is null ? new GeoAssetsDbContext(_dbOptions) : new GeoAssetsDbContext(_dbOptions, _organization);

    /// <summary>
    /// The rows a pushed-down read covers: for an organization, its own rows and the shared
    /// (<see cref="Guid.Empty"/>) ones — the query filter, which admits the organization's rows
    /// only, is replaced by that explicit predicate; without an organization, the context's rows.
    /// </summary>
    private IQueryable<GeoEntityRow> QueryableRows(GeoAssetsDbContext db)
    {
        if (_organization is null)
            return db.GeoEntities;

        var organizationId = _organization.CurrentOrganizationId;
        return db.GeoEntities
            .IgnoreQueryFilters()
            .Where(e => e.OrganizationId == organizationId || e.OrganizationId == Guid.Empty);
    }

    private sealed class Organization(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
