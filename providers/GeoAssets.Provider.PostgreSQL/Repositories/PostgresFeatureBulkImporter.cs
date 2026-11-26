using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// PostgreSQL implementation of <see cref="IFeatureBulkImporter"/>: each chunk is validated in memory
/// (<see cref="FeatureImportChunkValidator"/>), stamped with the organization and the import's provenance
/// (<see cref="FeatureImportAttributes.Scope"/>, <see cref="FeatureImportAttributes.ImportId"/>), mapped with the
/// provider's own row mapping (<see cref="GeoEntityRowMapper"/>), binary-copied into a transaction-scoped staging table
/// and upserted into <c>geo_entity</c> in one statement, with the written ids' <c>geo_entity_attribute</c> rows rewritten
/// from the staged attributes — all in one transaction that carries the organization for the Row-Level Security policy
/// (<see cref="FeatureImportStage"/>). Nothing goes through <see cref="PostgresAssetProvider"/>'s in-memory cache.
///
/// <para><b>Ownership.</b> An id already stored for another organization is not touched: the upsert's conflict clause only
/// updates the caller's own rows, and the id is reported <see cref="FeatureImportRejectionReasons.ForeignOrganization"/>.
/// Re-sending a chunk updates the same rows (the creation time stays the first write's).</para>
///
/// <para><b>Catalog.</b> <see cref="CompleteAsync"/> reloads the organization's cached catalog in
/// <see cref="PostgresOrganizationAssetCatalogs"/> once, when the host serves one; chunks do not.</para>
///
/// <para><b>Dirty tiles (XD01-432).</b> With an <see cref="ITileInvalidation"/>, each committed chunk reports the boxes its
/// written features had before and have after it, and a completion that replaced its scope the boxes of the features it
/// removed (<see cref="DirtyTileRegions"/>) — after the commit, never failing the import.</para>
/// </summary>
public sealed class PostgresFeatureBulkImporter : IFeatureBulkImporter
{
    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly TimeProvider _timeProvider;
    private readonly PostgresOrganizationAssetCatalogs? _catalogs;
    private readonly AfterCommitTileInvalidation? _tiles;

    /// <param name="dbOptions">Options for the per-call contexts (Npgsql with NetTopologySuite).</param>
    /// <param name="timeProvider">Stamps the written features' creation and update times.</param>
    /// <param name="catalogs">The host's organization catalogs to reload after an import completes; <c>null</c> when it has none.</param>
    /// <param name="tileInvalidation">Where committed writes report their dirty tiles; <c>null</c> = nowhere, nothing tracked.</param>
    /// <param name="logger">Logs a failed tile invalidation; <c>null</c> = not logged (still counted).</param>
    public PostgresFeatureBulkImporter(
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        TimeProvider timeProvider,
        PostgresOrganizationAssetCatalogs? catalogs = null,
        ITileInvalidation? tileInvalidation = null,
        ILogger<PostgresFeatureBulkImporter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _dbOptions = dbOptions;
        _timeProvider = timeProvider;
        _catalogs = catalogs;
        _tiles = AfterCommitTileInvalidation.For(tileInvalidation, logger ?? NullLogger<PostgresFeatureBulkImporter>.Instance);
    }

    public async Task<FeatureImportChunkResult> ImportChunkAsync(FeatureImportChunkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureImport(request.OrganizationId, request.ImportId, request.Scope);
        ArgumentNullException.ThrowIfNull(request.Features);
        if (request.Features.Count > FeatureImportLimits.MaxChunkSize)
        {
            throw new ArgumentException(
                $"A chunk holds at most {FeatureImportLimits.MaxChunkSize} features; this one holds {request.Features.Count}.",
                nameof(request));
        }

        var db = new GeoAssetsDbContext(_dbOptions);
        await using (db.ConfigureAwait(false))
        {
            var (result, dirty) = await db.Database.CreateExecutionStrategy()
                .ExecuteAsync(ct => WriteChunkAsync(db, request, ct), cancellationToken)
                .ConfigureAwait(false);
            await PublishDirtyTilesAsync(dirty).ConfigureAwait(false);
            return result;
        }
    }

    public async Task<FeatureImportCompletionResult> CompleteAsync(FeatureImportCompletion completion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completion);
        EnsureImport(completion.OrganizationId, completion.ImportId, completion.Scope);

        var removed = 0;
        if (completion.Mode == FeatureImportCompletionMode.ReplaceScope)
        {
            var db = new GeoAssetsDbContext(_dbOptions);
            await using (db.ConfigureAwait(false))
            {
                var removedBounds = await db.Database.CreateExecutionStrategy()
                    .ExecuteAsync(ct => DeleteScopeAsync(db, completion, ct), cancellationToken)
                    .ConfigureAwait(false);
                removed = removedBounds.Count;
                await PublishDirtyTilesAsync(removedBounds).ConfigureAwait(false);
            }
        }

        if (_catalogs is not null)
            await _catalogs.RefreshAsync(completion.OrganizationId, cancellationToken).ConfigureAwait(false);

        return new FeatureImportCompletionResult(removed);
    }

    /// <summary>The chunk's result, and the boxes its written features had before and have after it (none when nothing is tracked).</summary>
    private async Task<(FeatureImportChunkResult Result, IReadOnlyList<BoundingBox?> Dirty)> WriteChunkAsync(
        GeoAssetsDbContext db, FeatureImportChunkRequest request, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await FeatureImportStage.SetOrganizationAsync(connection, request.OrganizationId, ct).ConfigureAwait(false);

            var visibleAssetTypes = await VisibleAssetTypesAsync(db, request.OrganizationId, ct).ConfigureAwait(false);
            var chunk = FeatureImportChunkValidator.Validate(request.Features, visibleAssetTypes);
            var rejected = new List<FeatureImportRejection>(chunk.Rejected);
            if (chunk.Accepted.Count == 0)
            {
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return (new FeatureImportChunkResult(0, 0, rejected), []);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var rows = chunk.Accepted
                .Select(feature => GeoEntityRowMapper.MapToRow(Stamp(feature, request, now)))
                .ToList();

            await FeatureImportStage.StageAsync(connection, rows, ct).ConfigureAwait(false);
            var before = _tiles is null
                ? []
                : await FeatureImportStage.StoredBoundsAsync(connection, request.OrganizationId, ct).ConfigureAwait(false);
            var written = await FeatureImportStage.UpsertAsync(connection, ct).ConfigureAwait(false);
            rejected.AddRange(rows
                .Where(row => !written.ContainsKey(row.Id))
                .Select(row => new FeatureImportRejection(row.Id, FeatureImportRejectionReasons.ForeignOrganization)));

            if (written.Count > 0)
                await FeatureImportStage.ReplaceAttributesAsync(connection, [.. written.Keys], ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);

            IReadOnlyList<BoundingBox?> dirty = _tiles is null
                ? []
                : [.. before, .. rows.Where(row => written.ContainsKey(row.Id)).Select(row => DirtyTileRegions.BoundsOf(row.Geom?.EnvelopeInternal))];
            var inserted = written.Values.Count(wasInserted => wasInserted);
            return (new FeatureImportChunkResult(inserted, written.Count - inserted, rejected), dirty);
        }
    }

    /// <summary>Deletes the scope's features another import wrote; answers each removed feature's box (<c>null</c> without a geometry).</summary>
    private static async Task<IReadOnlyList<BoundingBox?>> DeleteScopeAsync(GeoAssetsDbContext db, FeatureImportCompletion completion, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await FeatureImportStage.SetOrganizationAsync(connection, completion.OrganizationId, ct).ConfigureAwait(false);
            var removed = await FeatureImportStage
                .DeleteScopeAsync(connection, completion.OrganizationId, completion.Scope, completion.ImportId, ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return removed;
        }
    }

    private Task PublishDirtyTilesAsync(IReadOnlyList<BoundingBox?> bounds) =>
        _tiles is null || bounds.Count == 0
            ? Task.CompletedTask
            : _tiles.PublishAsync(DirtyTileRegions.Of(bounds), AfterCommitTileInvalidation.BulkImportWriter);

    /// <summary>The organization's own asset types and the shared ones (<see cref="Guid.Empty"/>), with their allowed geometry.</summary>
    private static async Task<IReadOnlyDictionary<Guid, GeometryType?>> VisibleAssetTypesAsync(
        GeoAssetsDbContext db, Guid organizationId, CancellationToken ct)
    {
        var assetTypes = await db.AssetTypes
            .AsNoTracking()
            .Where(t => t.OrganizationId == organizationId || t.OrganizationId == Guid.Empty)
            .Select(t => new { t.Id, t.AllowedGeometryType })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return assetTypes.ToDictionary(t => t.Id, t => t.AllowedGeometryType);
    }

    /// <summary>Owner, canonical asset type id, provenance and times of a feature about to be written.</summary>
    private static GeoFeature Stamp(GeoFeature feature, FeatureImportChunkRequest request, DateTime now)
    {
        var properties = feature.Properties;
        properties.OrganizationId = request.OrganizationId;
        properties.AssetTypeId = Guid.Parse(properties.AssetTypeId).ToString();
        properties.CreatedAt = properties.CreatedAt == default ? now : AsUtc(properties.CreatedAt);
        properties.UpdatedAt = now;
        properties.CustomAttributes[FeatureImportAttributes.Scope] = request.Scope;
        properties.CustomAttributes[FeatureImportAttributes.ImportId] = request.ImportId;
        return feature;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static void EnsureImport(Guid organizationId, string importId, string scope)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("A feature import names the organization it writes for.", nameof(organizationId));

        ArgumentException.ThrowIfNullOrWhiteSpace(importId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
    }
}
