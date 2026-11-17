using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The asset catalog as one organization sees it (XD01-338): its own features plus the
/// organization-less (shared, <see cref="Guid.Empty"/>) ones, never another organization's.
/// Handed out per request by <see cref="PostgresOrganizationAssetCatalogs"/>.
///
/// <para><b>Two parts.</b> The organization's own <see cref="PostgresAssetProvider"/>, whose cache
/// holds that organization's features only, and the host's one shared catalog, whose cache holds
/// the shared features — so shared features are cached once per process, not once per
/// organization, and a write to them is seen by every organization at once.</para>
/// <list type="bullet">
///   <item>Cache-backed reads (<see cref="GetById"/>, <see cref="GetAll"/>, <see cref="Search"/>,
///   the in-memory spatial predicates and, through <see cref="GetAll"/>, the topology queries)
///   combine both parts.</item>
///   <item>Reads pushed down to PostgreSQL (<see cref="GetInBoundsAsync"/>,
///   <see cref="GetPageAsync"/>) are one query over both by the organization's provider, so paging
///   and the total count stay exact.</item>
///   <item>A write to an existing shared feature goes to the shared catalog; any other feature
///   write goes to the organization's provider, except a new feature stated as organization-less,
///   which goes to the shared catalog. <see cref="Clear"/> and <see cref="LoadAll"/> act on the
///   organization's own features only.</item>
///   <item>Asset types are read from the shared catalog, narrowed to the organization's and the
///   shared ones; deleting one the organization cannot see does nothing. Layers and layer rules
///   carry no organization and are the shared catalog's, as before.</item>
/// </list>
///
/// <para><b>No organization.</b> Built without an organization's provider, every member is the
/// shared catalog's, unchanged — what an unauthenticated or organization-less caller had before.</para>
///
/// <para>Deliberately not disposable: a request scope hands it out without owning it.
/// <see cref="PostgresOrganizationAssetCatalogs"/> owns the organization's provider.</para>
/// </summary>
public sealed class OrganizationAssetCatalog : IAssetProvider
{
    private readonly IAssetProvider _shared;
    private readonly IAssetProvider? _own;

    internal OrganizationAssetCatalog(Guid organizationId, IAssetProvider shared, IAssetProvider? own)
    {
        ArgumentNullException.ThrowIfNull(shared);

        OrganizationId = organizationId;
        _shared = shared;
        _own = own;
    }

    /// <summary>The organization this view is for; <see cref="Guid.Empty"/> for the shared-only view.</summary>
    public Guid OrganizationId { get; }

    // ── Read ───────────────────────────────────────────────────────────────────

    public GeoFeature? GetById(string id) => _own?.GetById(id) ?? _shared.GetById(id);

    public IReadOnlyList<GeoFeature> GetAll() => Combine(p => p.GetAll());

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => Combine(p => p.GetByAssetType(assetTypeId));

    public IReadOnlyList<GeoFeature> Search(string query) => Combine(p => p.Search(query));

    public Task<PagedResult<GeoFeature>> GetPageAsync(AssetQuery query) => Queried.GetPageAsync(query);

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => Combine(p => p.GetWithin(bounds));

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => Combine(p => p.GetIntersecting(geometry));

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Queried.GetInBoundsAsync(minLon, minLat, maxLon, maxLat);

    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Queried.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);

    public Task<string?> GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Queried.GetInBoundsRawJsonAsync(minLon, minLat, maxLon, maxLat);

    public IAsyncEnumerable<string> GetInBoundsRawJsonChunksAsync(
        double minLon, double minLat, double maxLon, double maxLat, CancellationToken ct = default) =>
        Queried.GetInBoundsRawJsonChunksAsync(minLon, minLat, maxLon, maxLat, ct);

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees)
    {
        ArgumentNullException.ThrowIfNull(center);

        if (_own is null)
            return _shared.GetNearby(center, distanceDegrees);

        return [.. _own.GetNearby(center, distanceDegrees)
            .Concat(_shared.GetNearby(center, distanceDegrees))
            .OrderBy(f => f.Geometry!.Distance(center))];
    }

    // ── Write ──────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        WriterFor(feature.Id, feature.Properties.OrganizationId).Add(feature);
    }

    public void Update(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        WriterFor(feature.Id, feature.Properties.OrganizationId).Update(feature);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (_own is null)
        {
            _shared.AddRange(features);
            return;
        }

        var byWriter = features.ToLookup(f => WriterFor(f.Id, f.Properties.OrganizationId));
        foreach (var group in byWriter)
            group.Key.AddRange([.. group]);
    }

    public void Delete(string id) => WriterFor(id, OrganizationId).Delete(id);

    public void Clear() => (_own ?? _shared).Clear();

    public void LoadAll(IEnumerable<GeoFeature> features) => (_own ?? _shared).LoadAll(features);

    // ── Asset types, layers, layer rules ───────────────────────────────────────

    public IReadOnlyList<AssetType> GetAssetTypes() =>
        _own is null
            ? _shared.GetAssetTypes()
            : [.. _shared.GetAssetTypes().Where(t => t.OrganizationId == OrganizationId || t.OrganizationId == Guid.Empty)];

    public void AddAssetType(AssetType assetType) => _shared.AddAssetType(assetType);

    public void DeleteAssetType(Guid id)
    {
        if (_own is not null && !GetAssetTypes().Any(t => t.Id == id))
            return;

        _shared.DeleteAssetType(id);
    }

    public IReadOnlyList<Layer> GetLayers() => _shared.GetLayers();

    public void AddLayer(Layer layer) => _shared.AddLayer(layer);

    public void DeleteLayer(Guid id) => _shared.DeleteLayer(id);

    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => _shared.GetLayerRules(assetTypeId);

    public void AddLayerRule(LayerRule layerRule) => _shared.AddLayerRule(layerRule);

    public void DeleteLayerRule(Guid id) => _shared.DeleteLayerRule(id);

    // ── Events (from both parts) ───────────────────────────────────────────────

    public event EventHandler<GeoFeature>? FeatureAdded
    {
        add
        {
            _shared.FeatureAdded += value;
            if (_own is not null) _own.FeatureAdded += value;
        }
        remove
        {
            _shared.FeatureAdded -= value;
            if (_own is not null) _own.FeatureAdded -= value;
        }
    }

    public event EventHandler<GeoFeature>? FeatureUpdated
    {
        add
        {
            _shared.FeatureUpdated += value;
            if (_own is not null) _own.FeatureUpdated += value;
        }
        remove
        {
            _shared.FeatureUpdated -= value;
            if (_own is not null) _own.FeatureUpdated -= value;
        }
    }

    public event EventHandler<string>? FeatureDeleted
    {
        add
        {
            _shared.FeatureDeleted += value;
            if (_own is not null) _own.FeatureDeleted += value;
        }
        remove
        {
            _shared.FeatureDeleted -= value;
            if (_own is not null) _own.FeatureDeleted -= value;
        }
    }

    public event EventHandler? CollectionChanged
    {
        add
        {
            _shared.CollectionChanged += value;
            if (_own is not null) _own.CollectionChanged += value;
        }
        remove
        {
            _shared.CollectionChanged -= value;
            if (_own is not null) _own.CollectionChanged -= value;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>The part whose SQL covers both the organization's and the shared rows.</summary>
    private IAssetProvider Queried => _own ?? _shared;

    private IReadOnlyList<GeoFeature> Combine(Func<IAssetProvider, IReadOnlyList<GeoFeature>> read) =>
        _own is null ? read(_shared) : [.. read(_own), .. read(_shared)];

    /// <summary>
    /// The part that owns the feature <paramref name="id"/>: the organization's when it has it, the
    /// shared catalog when that has it, otherwise — a feature neither part can see — the shared
    /// catalog for an organization-less <paramref name="organizationId"/> and the organization's
    /// provider for anything else.
    /// </summary>
    private IAssetProvider WriterFor(string id, Guid organizationId)
    {
        if (_own is null)
            return _shared;
        if (_own.GetById(id) is not null)
            return _own;
        if (_shared.GetById(id) is not null || organizationId == Guid.Empty)
            return _shared;
        return _own;
    }
}
