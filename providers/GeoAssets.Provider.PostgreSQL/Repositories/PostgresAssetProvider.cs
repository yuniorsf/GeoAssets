using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// PostgreSQL + PostGIS implementation of <see cref="IAssetProvider"/>.
/// Each instance owns its own <see cref="GeoAssetsDbContext"/> (scoped to one
/// logical collection / repository pool entry).
/// Topology queries use <see cref="IAssetProvider"/>'s default implementation
/// (<see cref="GeoAssets.Core.Services.TopoGraph"/> over <see cref="GetAll"/>) rather
/// than an explicit override — this provider's <see cref="GetAll"/> already returns the
/// exact same <see cref="Cache"/> those would have queried directly (XD01-135).
/// An organization's provider too large to cache whole serves its reads from PostgreSQL instead
/// (XD01-449, <c>PostgresAssetProvider.StoreReads.cs</c>).
/// </summary>
public sealed partial class PostgresAssetProvider : IAssetProvider, ISyncProvider, IAsyncDisposable
{
    private readonly GeoAssetsDbContext _db;
    private readonly DbContextOptions<GeoAssetsDbContext> _dbOptions;
    private readonly ILogger<PostgresAssetProvider> _logger;
    private readonly TimeProvider _timeProvider;

    // In-memory cache — rebuilt on first use and after writes
    private Dictionary<string, GeoFeature>? _cache;
    private List<AssetType>? _typeCache;
    private List<Layer>? _layerCache;
    private List<LayerRule>? _layerRuleCache;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    /// <param name="tileInvalidation">Where committed feature writes report their dirty tiles (XD01-432); <c>null</c> = nowhere, nothing tracked.</param>
    public PostgresAssetProvider(
        GeoAssetsDbContext db,
        DbContextOptions<GeoAssetsDbContext> dbOptions,
        ILogger<PostgresAssetProvider> logger,
        TimeProvider timeProvider,
        ITileInvalidation? tileInvalidation = null)
    {
        _db           = db;
        _dbOptions    = dbOptions;
        _logger       = logger;
        _timeProvider = timeProvider;
        _tiles        = AfterCommitTileInvalidation.For(tileInvalidation, logger);
    }

    // ── Cache helpers ──────────────────────────────────────────────────────────

    private Dictionary<string, GeoFeature> Cache =>
        _cache ??= LoadCacheFromDb();

    private Dictionary<string, GeoFeature> LoadCacheFromDb()
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        var rows = _db.GeoEntities.AsNoTracking().ToList();
        var cache = rows.Select(MapToFeature).ToDictionary(f => f.Id, StringComparer.Ordinal);
        var elapsedMs = _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
        LogCacheLoaded(_logger, rows.Count, elapsedMs);
        return cache;
    }

    /// <summary>Drops the whole cache and the by-id entries of <paramref name="ids"/> (all of them when <c>null</c>).</summary>
    private void InvalidateCache(IEnumerable<string>? ids = null)
    {
        _cache = null;
        if (ids is null)
            _byId?.Clear();
        else
            _byId?.Remove(ids);
    }

    /// <summary>
    /// Pre-warms <see cref="Cache"/> asynchronously via a short-lived context (mirrors
    /// <see cref="GetInBoundsAsync"/>'s pattern — never touches the long-lived <see cref="_db"/>
    /// instance the write methods use), so no synchronous caller falls through to
    /// <see cref="LoadCacheFromDb"/>'s blocking query. Called by
    /// <see cref="PostgresProviderFactory.CreateAsync"/> — <see cref="LoadCacheFromDb"/>/the lazy
    /// <see cref="Cache"/> property remain a defensive fallback only, not the primary path, once
    /// this has run (XD01-161).
    /// </summary>
    internal async Task WarmCacheAsync(CancellationToken ct = default)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        var db = NewContext();
        try
        {
            var rows = await db.GeoEntities.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
            _cache = rows.Select(MapToFeature).ToDictionary(f => f.Id, StringComparer.Ordinal);
            var elapsedMs = _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogCacheWarmed(_logger, rows.Count, elapsedMs);
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ── Read ───────────────────────────────────────────────────────────────────

    public GeoFeature? GetById(string id)
    {
        if (UsesStoreReads()) return StoreGetById(id);
        return Cache.TryGetValue(id, out var f) ? f : null;
    }

    public IReadOnlyList<GeoFeature> GetAll() =>
        UsesStoreReads() ? StoreEnumerate(nameof(GetAll), static (_, rows) => rows) : [.. Cache.Values];

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        UsesStoreReads()
            ? StoreEnumerate(nameof(GetByAssetType), (_, rows) => rows.Where(r => r.AssetTypeId == assetTypeId))
            : [.. Cache.Values.Where(f => string.Equals(f.Properties.AssetTypeId, assetTypeId, StringComparison.Ordinal))];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        if (UsesStoreReads()) return StoreSearch(query);
        return [.. Cache.Values.Where(f =>
            f.Properties.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.CustomAttributes.Any(kv =>
                kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))];
    }

    // ── Spatial ────────────────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!UsesStoreReads())
            return [.. Cache.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

        var area = bounds.NtsGeometry;
        return StoreEnumerate(nameof(GetWithin), (_, rows) => rows.Where(r => r.Geom != null && r.Geom.Within(area)));
    }

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (!UsesStoreReads())
            return [.. Cache.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

        var area = geometry.NtsGeometry;
        return StoreEnumerate(nameof(GetIntersecting), (_, rows) => rows.Where(r => r.Geom != null && r.Geom.Intersects(area)));
    }

    /// <summary>
    /// Pushes the bbox filter to PostGIS via ST_Intersects — never loads the full table.
    /// EF Core + Npgsql translates <c>.Intersects()</c> to <c>ST_Intersects</c> in SQL.
    /// </summary>
    public async Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        var bbox    = factory.ToGeometry(new Envelope(minLon, maxLon, minLat, maxLat));
        bbox.SRID   = 4326;

        // Use a dedicated short-lived context so concurrent viewport requests
        // never share the same DbContext instance (which would cause a concurrency exception).
        var db = NewContext();
        try
        {
            var rows = await QueryableRows(db)
                .AsNoTracking()
                .Where(e => e.Geom != null && e.Geom.Intersects(bbox))
                .ToListAsync()
                .ConfigureAwait(false);

            return rows.Select(MapToFeature).ToList();
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(
        double minLon, double minLat, double maxLon, double maxLat)
    {
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        var opts = GeoJsonSerializer.CompactOptions;
        return [.. features.Select(f => JsonSerializer.SerializeToElement(f, opts))];
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        UsesStoreReads()
            ? StoreNearby(center, distanceDegrees)
            : [.. Cache.Values
                .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
                .OrderBy(f => f.Geometry!.Distance(center))];

    // ── Paged/filtered queries ───────────────────────────────────────────────────

    /// <summary>
    /// Pushes filtering, counting and paging to PostgreSQL — never touches <see cref="Cache"/>
    /// and never materializes the full <c>geo_entity</c> table, unlike every other read on this
    /// class. Uses a dedicated short-lived <see cref="GeoAssetsDbContext"/>, mirroring
    /// <see cref="GetInBoundsAsync"/>, so concurrent page requests don't share a context.
    /// <see cref="AssetQuery.SearchText"/> matches <c>Name</c>, <c>Description</c>, and
    /// <see cref="GeoFeatureProperties.CustomAttributes"/> keys/values — the same fields
    /// <see cref="Search"/> matches — for parity across <see cref="IAssetProvider"/>
    /// implementers (XD01-123).
    /// </summary>
    public async Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var db = NewContext();
        try
        {
            var rows = await ApplyPageQueryFiltersAsync(db, query).ConfigureAwait(false);
            var totalCount = await rows.CountAsync().ConfigureAwait(false);

            rows = query.SortBy switch
            {
                "name"      => query.SortDescending ? rows.OrderByDescending(r => r.Name)      : rows.OrderBy(r => r.Name),
                "createdAt" => query.SortDescending ? rows.OrderByDescending(r => r.CreatedAt) : rows.OrderBy(r => r.CreatedAt),
                "updatedAt" => query.SortDescending ? rows.OrderByDescending(r => r.UpdatedAt) : rows.OrderBy(r => r.UpdatedAt),
                _           => query.SortDescending ? rows.OrderByDescending(r => r.Id)        : rows.OrderBy(r => r.Id)
            };

            // OFFSET still walks past skipped rows even with a perfect index, so deep pages degrade
            // linearly regardless of indexing — keyset pagination would be the real fix if that
            // becomes a problem (XD01-171).
            var page = await rows.Skip(query.Skip).Take(query.Take).ToListAsync().ConfigureAwait(false);
            return new PagedResult<GeoFeature> { Items = page.Select(MapToFeature).ToList(), TotalCount = totalCount };
        }
        finally
        {
            await db.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<IQueryable<GeoEntityRow>> ApplyPageQueryFiltersAsync(
        GeoAssetsDbContext db, AssetQuery query)
    {
        var rows = QueryableRows(db).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.AssetTypeId))
            rows = rows.Where(r => r.AssetTypeId == query.AssetTypeId);

        if (query.OrganizationId is { } orgId)
            rows = rows.Where(r => r.OrganizationId == orgId);

        if (query.FeatureIds is { Count: > 0 } featureIds)
            rows = rows.Where(r => featureIds.Contains(r.Id));

        if (query.AttributeFilters is { Count: > 0 } attrFilters)
        {
            foreach (var (key, value) in attrFilters)
            {
                var matchingIdsForKey = await db.GeoEntityAttributes
                    .Where(a => a.Key == key && a.Value == value)
                    .Select(a => a.EntityId)
                    .Distinct()
                    .ToListAsync()
                    .ConfigureAwait(false);

                rows = rows.Where(r => matchingIdsForKey.Contains(r.Id));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var pattern = $"%{query.SearchText}%";

            // geo_entity_attribute is a normalized, trigram-indexed copy of custom_attributes
            // (XD01-171). Precomputing the matching ids keeps the Name/Description trigram indexes
            // usable below and matches Search()'s CustomAttributes.Any(key/value) semantics (XD01-123).
            var matchingAttributeIds = await db.GeoEntityAttributes
                .Where(a => EF.Functions.ILike(a.Key, pattern) || EF.Functions.ILike(a.Value, pattern))
                .Select(a => a.EntityId)
                .Distinct()
                .ToListAsync()
                .ConfigureAwait(false);

            rows = rows.Where(r =>
                EF.Functions.ILike(r.Name, pattern) ||
                EF.Functions.ILike(r.Description, pattern) ||
                matchingAttributeIds.Contains(r.Id));
        }

        return rows;
    }

    // ── Write ──────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        StampOrganization(feature);
        var row = GeoEntityRowMapper.MapToRow(feature);
        _db.GeoEntities.Add(row);
        _db.GeoEntityAttributes.AddRange(GeoEntityRowMapper.MapToAttributeRows(feature));
        SaveChanges();
        PublishDirtyTiles([BoundsOf(feature)]);
        InvalidateCache([feature.Id]);
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        RefuseRowsOfAnotherOrganization([feature.Id]);
        StampOrganization(feature);
        feature.Properties.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var row = GeoEntityRowMapper.MapToRow(feature);
        var before = StoredBounds([feature.Id]);
        SaveChangesInTransaction(() =>
        {
            _db.GeoEntityAttributes.Where(a => a.EntityId == feature.Id).ExecuteDelete();
            _db.GeoEntityAttributes.AddRange(GeoEntityRowMapper.MapToAttributeRows(feature));
            _db.GeoEntities.Update(row);
        });
        PublishDirtyTiles([.. before, BoundsOf(feature)]);
        InvalidateCache([feature.Id]);
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        var list = features.ToList();
        var ids = list.Select(f => f.Id).ToList();
        RefuseRowsOfAnotherOrganization(ids);
        foreach (var f in list)
            StampOrganization(f);
        var before = StoredBounds(ids);
        SaveChangesInTransaction(() =>
        {
            // Upsert: the ids already stored are updated, the others inserted (XD01-341).
            var existing = _db.GeoEntities.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => e.Id)
                .ToHashSet(StringComparer.Ordinal);
            _db.GeoEntityAttributes.Where(a => ids.Contains(a.EntityId)).ExecuteDelete();
            foreach (var f in list)
            {
                var row = GeoEntityRowMapper.MapToRow(f);
                if (existing.Contains(f.Id))
                    _db.GeoEntities.Update(row);
                else
                    _db.GeoEntities.Add(row);
                _db.GeoEntityAttributes.AddRange(GeoEntityRowMapper.MapToAttributeRows(f));
            }
        });
        PublishDirtyTiles([.. before, .. list.Select(BoundsOf)]);
        InvalidateCache(ids);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        var row = _db.GeoEntities.Find(id);
        if (row is null) return;
        // geo_entity_attribute rows for this id are cleaned up via DB-level FK cascade.
        _db.GeoEntities.Remove(row);
        SaveChanges();
        PublishDirtyTiles([BoundsOf(row)]);
        InvalidateCache([id]);
        FeatureDeleted?.Invoke(this, id);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        var removed = _db.GeoEntities.ToList();
        _db.GeoEntities.RemoveRange(removed);
        SaveChanges();
        PublishDirtyTiles(removed.Select(BoundsOf));
        InvalidateCache();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var list = features.ToList();
        RefuseRowsOfAnotherOrganization([.. list.Select(f => f.Id)]);
        var removed = _db.GeoEntities.ToList();
        _db.GeoEntities.RemoveRange(removed);
        foreach (var f in list)
        {
            StampOrganization(f);
            _db.GeoEntities.Add(GeoEntityRowMapper.MapToRow(f));
            _db.GeoEntityAttributes.AddRange(GeoEntityRowMapper.MapToAttributeRows(f));
        }
        SaveChanges();
        PublishDirtyTiles([.. removed.Select(BoundsOf), .. list.Select(BoundsOf)]);
        InvalidateCache();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Asset types ────────────────────────────────────────────────────────────

    public IReadOnlyList<AssetType> GetAssetTypes()
    {
        _typeCache ??= _db.AssetTypes.AsNoTracking()
            .Select(r => new AssetType
            {
                Id        = r.Id,
                Name      = r.Name,
                Color     = r.Color,
                IconUrl   = r.IconUrl,
                IsBuiltIn = r.IsBuiltIn,
                IsProtected = r.IsProtected,
                AllowedGeometryType = r.AllowedGeometryType,
                DefaultLayerId = r.DefaultLayerId,
                AttributesSchemaJson = r.AttributesSchemaJson,
                MinZoom = r.MinZoom,
                MaxZoom = r.MaxZoom,
                Roles = r.Roles,
                OrganizationId = r.OrganizationId
            }).ToList();
        return [.. _typeCache];
    }

    public void AddAssetType(AssetType assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (_db.AssetTypes.Find(assetType.Id) is not null) return;
        _db.AssetTypes.Add(new AssetTypeRow
        {
            Id        = assetType.Id,
            Name      = assetType.Name,
            Color     = assetType.Color,
            IconUrl   = assetType.IconUrl,
            IsBuiltIn = assetType.IsBuiltIn,
            IsProtected = assetType.IsProtected,
            AllowedGeometryType = assetType.AllowedGeometryType,
            DefaultLayerId = assetType.DefaultLayerId,
            AttributesSchemaJson = assetType.AttributesSchemaJson,
            MinZoom = assetType.MinZoom,
            MaxZoom = assetType.MaxZoom,
            Roles = [.. assetType.Roles],
            OrganizationId = assetType.OrganizationId
        });
        SaveChanges();
        _typeCache = null;
    }

    public void DeleteAssetType(Guid id)
    {
        var row = _db.AssetTypes.Find(id);
        if (row is null || row.IsBuiltIn) return;
        _db.AssetTypes.Remove(row);
        SaveChanges();
        _typeCache = null;
    }

    // ── Layers ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<Layer> GetLayers()
    {
        _layerCache ??= _db.Layers.AsNoTracking()
            .Select(r => new Layer
            {
                Id          = r.Id,
                Name        = r.Name,
                GeometryType = r.GeometryType,
                Color       = r.Color,
                Radius      = r.Radius,
                IconUrl     = r.IconUrl,
                Weight      = r.Weight,
                DashArray   = r.DashArray,
                FillColor   = r.FillColor,
                FillOpacity = r.FillOpacity
            }).ToList();
        return [.. _layerCache];
    }

    public void AddLayer(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_db.Layers.Find(layer.Id) is not null) return;
        _db.Layers.Add(new LayerRow
        {
            Id          = layer.Id,
            Name        = layer.Name,
            GeometryType = layer.GeometryType,
            Color       = layer.Color,
            Radius      = layer.Radius,
            IconUrl     = layer.IconUrl,
            Weight      = layer.Weight,
            DashArray   = layer.DashArray,
            FillColor   = layer.FillColor,
            FillOpacity = layer.FillOpacity
        });
        SaveChanges();
        _layerCache = null;
    }

    public void DeleteLayer(Guid id)
    {
        var row = _db.Layers.Find(id);
        if (row is null) return;
        _db.Layers.Remove(row);
        SaveChanges();
        _layerCache = null;
    }

    // ── Layer rules ────────────────────────────────────────────────────────────

    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId)
    {
        _layerRuleCache ??= _db.LayerRules.AsNoTracking()
            .Include(r => r.Conditions)
            .ToList()
            .Select(r => new LayerRule
            {
                Id          = r.Id,
                AssetTypeId = r.AssetTypeId,
                LayerId     = r.LayerId,
                Priority    = r.Priority,
                Conditions  = [.. r.Conditions.Select(c => new LayerRuleCondition
                {
                    Attribute = c.Attribute,
                    Operator  = c.Operator,
                    Value     = c.Value
                })]
            }).ToList();
        return [.. _layerRuleCache.Where(r => r.AssetTypeId == assetTypeId)];
    }

    public void AddLayerRule(LayerRule layerRule)
    {
        ArgumentNullException.ThrowIfNull(layerRule);
        if (_db.LayerRules.Find(layerRule.Id) is not null) return;
        _db.LayerRules.Add(new LayerRuleRow
        {
            Id          = layerRule.Id,
            AssetTypeId = layerRule.AssetTypeId,
            LayerId     = layerRule.LayerId,
            Priority    = layerRule.Priority,
            Conditions  = [.. layerRule.Conditions.Select(c => new LayerRuleConditionRow
            {
                Attribute = c.Attribute,
                Operator  = c.Operator,
                Value     = c.Value
            })]
        });
        SaveChanges();
        _layerRuleCache = null;
    }

    public void DeleteLayerRule(Guid id)
    {
        var row = _db.LayerRules.Find(id);
        if (row is null) return;
        _db.LayerRules.Remove(row);
        SaveChanges();
        _layerRuleCache = null;
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    internal static GeoFeature MapToFeature(GeoEntityRow row)
    {
        var attrs = JsonSerializer.Deserialize<Dictionary<string, string>>(row.CustomAttributesJson, _json)
                    ?? [];
        var topology = JsonSerializer.Deserialize<List<TopoEdge>>(row.TopologyJson, _json)
                       ?? [];

        return new GeoFeature
        {
            Id       = row.Id,
            Geometry = row.Geom is null ? null : GeoGeometry.FromNts(row.Geom),
            Topology = topology,
            Properties = new GeoFeatureProperties
            {
                Name             = row.Name,
                AssetTypeId      = row.AssetTypeId,
                Description      = row.Description,
                LayerId          = row.LayerId?.ToString() ?? string.Empty,
                OrganizationId   = row.OrganizationId,
                CreatedAt        = row.CreatedAt,
                UpdatedAt        = row.UpdatedAt,
                // SRID comes from the PostGIS geometry (authoritative); fall back to 4326
                Srid             = row.Geom?.SRID ?? 4326,
                CustomAttributes = attrs
            }
        };
    }

    /// <summary>
    /// Saves the long-lived <see cref="_db"/>'s pending changes, then stops tracking every entity,
    /// saved or not: the next write maps fresh row instances for the same keys (a second
    /// <see cref="Update"/> of a feature, an upsert of a stored id), which a still-tracked
    /// instance would refuse, and a failed write must not leave its changes pending for the next
    /// one (XD01-341).
    /// </summary>
    private void SaveChanges()
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        try
        {
            var rowCount = _db.SaveChanges();
            var elapsedMs = _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            LogChangesSaved(_logger, rowCount, elapsedMs);
        }
        catch (Exception ex)
        {
            LogSaveChangesFailed(_logger, ex);
            throw;
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Runs <paramref name="stage"/> — which may write directly (<c>ExecuteDelete</c>) as well as
    /// stage tracked changes — and saves, in one transaction, so a failed write leaves every row
    /// as it was (XD01-341). Inside a transaction the context's owner already began (as
    /// <see cref="PostgresAssetMutationBatchApplier"/> does), that one is used; otherwise one is
    /// begun under the context's execution strategy, which a retrying one requires of a
    /// user-initiated transaction.
    /// </summary>
    private void SaveChangesInTransaction(Action stage)
    {
        if (_db.Database.CurrentTransaction is not null)
        {
            StageAndSaveChanges(stage);
            return;
        }

        _db.Database.CreateExecutionStrategy().Execute(() =>
        {
            using var transaction = _db.Database.BeginTransaction();
            StageAndSaveChanges(stage);
            transaction.Commit();
        });
    }

    /// <summary>A failed <paramref name="stage"/> leaves nothing tracked either, as <see cref="SaveChanges"/> does.</summary>
    private void StageAndSaveChanges(Action stage)
    {
        try
        {
            stage();
            SaveChanges();
        }
        finally
        {
            _db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgresAssetProvider.LoadCacheFromDb — {RowCount} rows in {ElapsedMs:F1} ms")]
    private static partial void LogCacheLoaded(ILogger logger, int rowCount, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgresAssetProvider.WarmCacheAsync — {RowCount} rows in {ElapsedMs:F1} ms")]
    private static partial void LogCacheWarmed(ILogger logger, int rowCount, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgresAssetProvider.SaveChanges — {RowCount} rows in {ElapsedMs:F1} ms")]
    private static partial void LogChangesSaved(ILogger logger, int rowCount, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgresAssetProvider: SaveChanges failed")]
    private static partial void LogSaveChangesFailed(ILogger logger, Exception ex);

    public ValueTask DisposeAsync() => _db.DisposeAsync();
}
