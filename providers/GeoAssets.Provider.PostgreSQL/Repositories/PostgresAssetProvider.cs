using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// PostgreSQL + PostGIS implementation of <see cref="IAssetProvider"/>.
/// Each instance owns its own <see cref="GeoAssetsDbContext"/> (scoped to one
/// logical collection / repository pool entry).
/// Topology and spatial graph queries fall back to the NTS-backed helpers from
/// <see cref="GeoAssets.Core.Services.TopoGraph"/>.
/// </summary>
public sealed class PostgresAssetProvider : IAssetProvider, IAsyncDisposable
{
    private readonly GeoAssetsDbContext _db;
    private readonly ILogger<PostgresAssetProvider> _logger;

    // In-memory cache — rebuilt on first use and after writes
    private Dictionary<string, GeoFeature>? _cache;
    private List<AssetType>? _typeCache;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public event EventHandler<GeoFeature>? FeatureAdded;
    public event EventHandler<GeoFeature>? FeatureUpdated;
    public event EventHandler<string>? FeatureDeleted;
    public event EventHandler? CollectionChanged;

    public PostgresAssetProvider(GeoAssetsDbContext db, ILogger<PostgresAssetProvider> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Cache helpers ──────────────────────────────────────────────────────────

    private Dictionary<string, GeoFeature> Cache =>
        _cache ??= LoadCacheFromDb();

    private Dictionary<string, GeoFeature> LoadCacheFromDb()
    {
        var rows = _db.GeoEntities.AsNoTracking().ToList();
        return rows.Select(MapToFeature).ToDictionary(f => f.Id);
    }

    private void InvalidateCache() => _cache = null;

    // ── Read ───────────────────────────────────────────────────────────────────

    public GeoFeature? GetById(string id) =>
        Cache.TryGetValue(id, out var f) ? f : null;

    public IReadOnlyList<GeoFeature> GetAll() => [.. Cache.Values];

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) =>
        [.. Cache.Values.Where(f => f.Properties.AssetTypeId == assetTypeId)];

    public IReadOnlyList<GeoFeature> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetAll();
        var lower = query.ToLowerInvariant();
        return [.. Cache.Values.Where(f =>
            f.Properties.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.Description.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
            f.Properties.CustomAttributes.Any(kv =>
                kv.Key.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                kv.Value.Contains(lower, StringComparison.OrdinalIgnoreCase)))];
    }

    // ── Spatial ────────────────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) =>
        [.. Cache.Values.Where(f => f.Geometry is not null && f.Geometry.Within(bounds))];

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) =>
        [.. Cache.Values.Where(f => f.Geometry is not null && f.Geometry.Intersects(geometry))];

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        [.. Cache.Values
            .Where(f => f.Geometry is not null && f.Geometry.Distance(center) <= distanceDegrees)
            .OrderBy(f => f.Geometry!.Distance(center))];

    // ── Topology ───────────────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetNeighbors(string id) =>
        GeoAssets.Core.Services.TopoGraph.GetNeighbors(id, Cache.Values);

    public IReadOnlyList<GeoFeature> GetDescendants(string id) =>
        GeoAssets.Core.Services.TopoGraph.GetDescendants(id, Cache.Values);

    public IReadOnlyList<GeoFeature> GetAncestors(string id) =>
        GeoAssets.Core.Services.TopoGraph.GetAncestors(id, Cache.Values);

    public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) =>
        GeoAssets.Core.Services.TopoGraph.FindPath(fromId, toId, Cache.Values);

    public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) =>
        GeoAssets.Core.Services.TopoGraph.FindShortestPath(fromId, toId, Cache.Values);

    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() =>
        GeoAssets.Core.Services.TopoGraph.GetConnectedComponents(Cache.Values);

    public bool HasCycles() =>
        GeoAssets.Core.Services.TopoGraph.HasCycles(Cache.Values);

    public IReadOnlyList<GeoFeature> TopologicalSort() =>
        GeoAssets.Core.Services.TopoGraph.TopologicalSort(Cache.Values);

    // ── Write ──────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        var row = MapToRow(feature);
        _db.GeoEntities.Add(row);
        SaveChanges();
        InvalidateCache();
        FeatureAdded?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Update(GeoFeature feature)
    {
        feature.Properties.UpdatedAt = DateTime.UtcNow;
        var row = MapToRow(feature);
        _db.GeoEntities.Update(row);
        SaveChanges();
        InvalidateCache();
        FeatureUpdated?.Invoke(this, feature);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        var list = features.ToList();
        foreach (var f in list)
        {
            var row = MapToRow(f);
            // Upsert: EF will track whether to insert or update
            if (_db.GeoEntities.Find(f.Id) is null)
                _db.GeoEntities.Add(row);
            else
                _db.GeoEntities.Update(row);
        }
        SaveChanges();
        InvalidateCache();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        var row = _db.GeoEntities.Find(id);
        if (row is null) return;
        _db.GeoEntities.Remove(row);
        SaveChanges();
        InvalidateCache();
        FeatureDeleted?.Invoke(this, id);
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _db.GeoEntities.RemoveRange(_db.GeoEntities);
        SaveChanges();
        InvalidateCache();
        CollectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        _db.GeoEntities.RemoveRange(_db.GeoEntities);
        foreach (var f in features)
            _db.GeoEntities.Add(MapToRow(f));
        SaveChanges();
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
                IsBuiltIn = r.IsBuiltIn
            }).ToList();
        return [.. _typeCache];
    }

    public void AddAssetType(AssetType assetType)
    {
        if (_db.AssetTypes.Find(assetType.Id) is not null) return;
        _db.AssetTypes.Add(new AssetTypeRow
        {
            Id        = assetType.Id,
            Name      = assetType.Name,
            Color     = assetType.Color,
            IconUrl   = assetType.IconUrl,
            IsBuiltIn = assetType.IsBuiltIn
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

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static GeoFeature MapToFeature(GeoEntityRow row)
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
                LayerId          = row.LayerId,
                CreatedAt        = row.CreatedAt,
                UpdatedAt        = row.UpdatedAt,
                // SRID comes from the PostGIS geometry (authoritative); fall back to 4326
                Srid             = row.Geom?.SRID ?? 4326,
                CustomAttributes = attrs
            }
        };
    }

    private static GeoEntityRow MapToRow(GeoFeature f) =>
        new()
        {
            Id                   = f.Id,
            Name                 = f.Properties.Name,
            AssetTypeId          = f.Properties.AssetTypeId,
            Description          = f.Properties.Description,
            LayerId              = f.Properties.LayerId,
            CreatedAt            = f.Properties.CreatedAt,
            UpdatedAt            = f.Properties.UpdatedAt,
            Geom                 = f.Geometry?.NtsGeometry,
            CustomAttributesJson = JsonSerializer.Serialize(f.Properties.CustomAttributes, _json),
            TopologyJson         = JsonSerializer.Serialize(f.Topology, _json)
        };

    private void SaveChanges()
    {
        try
        {
            _db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgresAssetProvider: SaveChanges failed");
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
