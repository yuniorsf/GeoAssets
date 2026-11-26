using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
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
/// pushes down to PostgreSQL (<see cref="GetInBoundsAsync"/>, <see cref="GetInViewportAsync"/>, <see cref="GetPageAsync"/>) return
/// that organization's features plus the organization-less (shared) ones in one query, so their
/// paging and counting stay in SQL; <see cref="OrganizationAssetCatalog"/> adds the shared
/// features to the cache-backed reads from the shared catalog.</para>
///
/// <para>Built without an organization (the public constructor), nothing changes: contexts are
/// opened without a tenant, as before.</para>
///
/// <para><b>Writes (XD01-340).</b> Either way, an update, upsert or load naming an id whose row is
/// outside the provider's rows (another organization's) is refused with
/// <see cref="ForeignGeoFeatureWriteException"/> before anything is written. An organization's
/// provider also stamps its organization on every feature it writes.</para>
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
        TimeProvider timeProvider,
        ITileInvalidation? tileInvalidation = null)
        : this(dbOptions, OrganizationOf(organizationId), logger, timeProvider, tileInvalidation)
    {
    }

    private PostgresAssetProvider(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        ITenantContext organization,
        ILogger<PostgresAssetProvider> logger,
        TimeProvider timeProvider,
        ITileInvalidation? tileInvalidation)
        : this(new GeoAssetsDbContext(dbOptions, organization), dbOptions, logger, timeProvider, tileInvalidation)
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

    /// <summary>
    /// For an organization's provider, stamps its organization on <paramref name="feature"/>, so
    /// it never writes a row of another organization or an organization-less one, whatever the
    /// feature states (XD01-340); without an organization, the feature's own is kept, as before.
    /// </summary>
    private void StampOrganization(GeoFeature feature)
    {
        if (_organization is not null)
            feature.Properties.OrganizationId = _organization.CurrentOrganizationId;
    }

    /// <summary>
    /// Refuses a write naming an id whose row exists but is outside this provider's rows (the
    /// long-lived context's query filter: its organization's, or the organization-less ones
    /// without one) — an update or upsert by id would otherwise overwrite another organization's
    /// row, which the filter hides from every read but not from <c>UPDATE … WHERE id</c>
    /// (XD01-340). Checked before anything is written.
    /// </summary>
    /// <exception cref="ForeignGeoFeatureWriteException">Some of <paramref name="ids"/> belong to another organization.</exception>
    private void RefuseRowsOfAnotherOrganization(List<string> ids)
    {
        if (ids.Count == 0)
            return;

        var existing = _db.GeoEntities.IgnoreQueryFilters().AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => e.Id)
            .ToList();
        if (existing.Count == 0)
            return;

        var own = _db.GeoEntities.AsNoTracking()
            .Where(e => existing.Contains(e.Id))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        var foreign = existing.Where(id => !own.Contains(id)).ToList();
        if (foreign.Count > 0)
            throw new ForeignGeoFeatureWriteException(foreign);
    }

    private sealed class Organization(Guid organizationId) : ITenantContext
    {
        public Guid CurrentOrganizationId { get; } = organizationId;
    }
}
