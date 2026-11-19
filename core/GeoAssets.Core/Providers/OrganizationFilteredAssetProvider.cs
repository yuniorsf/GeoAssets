using System.Collections.Concurrent;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Providers;

/// <summary>
/// The catalog as one organization sees it (XD01-373), over a provider holding every organization's features — the
/// generic counterpart of a provider's native organization scoping (<see cref="FilteringOrganizationCatalogFactory"/>).
/// <list type="bullet">
///   <item>Reads see the organization's own features plus the shared (<see cref="Guid.Empty"/>) ones; asset types
///   likewise. Paged and topology reads go through the interface's defaults over the filtered reads, and the raw-JSON
///   path is not offered, so nothing unfiltered reaches a caller.</item>
///   <item>A write to a new feature or to one of the organization's own is stamped with the organization; a shared
///   feature stays shared; a write naming another organization's feature is refused
///   (<see cref="ForeignGeoFeatureWriteException"/>) and changes nothing. Deleting a feature the organization cannot see
///   does nothing. <see cref="Clear"/> and <see cref="LoadAll"/> act on the organization's own features only.</item>
///   <item>Feature events reach a subscriber only for features it can see; <see cref="FeatureDeleted"/> carries ids only.</item>
/// </list>
/// For <see cref="Guid.Empty"/> the view is the shared features alone. It owns nothing and is not disposable.
/// </summary>
public sealed class OrganizationFilteredAssetProvider : IAssetProvider, IProviderDecorator
{
    private readonly IAssetProvider _inner;
    private readonly ConcurrentDictionary<EventHandler<GeoFeature>, EventHandler<GeoFeature>> _added = new();
    private readonly ConcurrentDictionary<EventHandler<GeoFeature>, EventHandler<GeoFeature>> _updated = new();

    public OrganizationFilteredAssetProvider(Guid organizationId, IAssetProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        OrganizationId = organizationId;
        _inner = inner;
    }

    /// <summary>The organization this view is for; <see cref="Guid.Empty"/> for the shared-only view.</summary>
    public Guid OrganizationId { get; }

    IAssetProvider IProviderDecorator.Inner => _inner;

    // ── Read ───────────────────────────────────────────────────────────────────

    public GeoFeature? GetById(string id) => _inner.GetById(id) is { } feature && IsVisible(feature) ? feature : null;

    public IReadOnlyList<GeoFeature> GetAll() => Visible(_inner.GetAll());

    public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => Visible(_inner.GetByAssetType(assetTypeId));

    public IReadOnlyList<GeoFeature> Search(string query) => Visible(_inner.Search(query));

    public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => Visible(_inner.GetWithin(bounds));

    public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => Visible(_inner.GetIntersecting(geometry));

    public async Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        Visible(await _inner.GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false));

    public async Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat)
    {
        var features = await GetInBoundsAsync(minLon, minLat, maxLon, maxLat).ConfigureAwait(false);
        var options = GeoJsonSerializer.CompactOptions;
        return [.. features.Select(f => JsonSerializer.SerializeToElement(f, options))];
    }

    public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) =>
        Visible(_inner.GetNearby(center, distanceDegrees));

    // ── Write ──────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        StampOrRefuse([feature]);
        _inner.Add(feature);
    }

    public void Update(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        StampOrRefuse([feature]);
        _inner.Update(feature);
    }

    public void AddRange(IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var list = features.ToList();
        StampOrRefuse(list);
        _inner.AddRange(list);
    }

    public void Delete(string id)
    {
        if (GetById(id) is not null) _inner.Delete(id);
    }

    public void Clear()
    {
        foreach (var feature in Own())
            _inner.Delete(feature.Id);
    }

    public void LoadAll(IEnumerable<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var list = features.ToList();
        StampOrRefuse(list);
        Clear();
        _inner.AddRange(list);
    }

    // ── Asset types, layers, layer rules ───────────────────────────────────────

    public IReadOnlyList<AssetType> GetAssetTypes() =>
        [.. _inner.GetAssetTypes().Where(t => t.OrganizationId == OrganizationId || t.OrganizationId == Guid.Empty)];

    public void AddAssetType(AssetType assetType) => _inner.AddAssetType(assetType);

    public void DeleteAssetType(Guid id)
    {
        if (GetAssetTypes().Any(t => t.Id == id)) _inner.DeleteAssetType(id);
    }

    public IReadOnlyList<Layer> GetLayers() => _inner.GetLayers();

    public void AddLayer(Layer layer) => _inner.AddLayer(layer);

    public void DeleteLayer(Guid id) => _inner.DeleteLayer(id);

    public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => _inner.GetLayerRules(assetTypeId);

    public void AddLayerRule(LayerRule layerRule) => _inner.AddLayerRule(layerRule);

    public void DeleteLayerRule(Guid id) => _inner.DeleteLayerRule(id);

    // ── Events (visible features only) ─────────────────────────────────────────

    public event EventHandler<GeoFeature>? FeatureAdded
    {
        add => Subscribe(value, _added, h => _inner.FeatureAdded += h);
        remove => Unsubscribe(value, _added, h => _inner.FeatureAdded -= h);
    }

    public event EventHandler<GeoFeature>? FeatureUpdated
    {
        add => Subscribe(value, _updated, h => _inner.FeatureUpdated += h);
        remove => Unsubscribe(value, _updated, h => _inner.FeatureUpdated -= h);
    }

    public event EventHandler<string>? FeatureDeleted
    {
        add => _inner.FeatureDeleted += value;
        remove => _inner.FeatureDeleted -= value;
    }

    public event EventHandler? CollectionChanged
    {
        add => _inner.CollectionChanged += value;
        remove => _inner.CollectionChanged -= value;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private bool IsVisible(GeoFeature feature) =>
        feature.Properties.OrganizationId == OrganizationId || feature.Properties.OrganizationId == Guid.Empty;

    private List<GeoFeature> Visible(IEnumerable<GeoFeature> features) => [.. features.Where(IsVisible)];

    private List<GeoFeature> Own() =>
        _inner.GetAll().Where(f => f.Properties.OrganizationId == OrganizationId).ToList();

    /// <summary>
    /// Refuses the whole write when it names another organization's feature; otherwise stamps each feature: an existing
    /// shared one stays shared, any other gets this view's organization — never the one the feature states.
    /// </summary>
    private void StampOrRefuse(IReadOnlyList<GeoFeature> features)
    {
        var existing = features.Select(f => (Feature: f, Stored: _inner.GetById(f.Id))).ToList();
        var foreign = existing
            .Where(e => e.Stored is { } stored && !IsVisible(stored))
            .Select(e => e.Feature.Id)
            .ToList();
        if (foreign.Count > 0) throw new ForeignGeoFeatureWriteException(foreign);

        foreach (var (feature, stored) in existing)
        {
            feature.Properties.OrganizationId = stored is { } s && s.Properties.OrganizationId == Guid.Empty
                ? Guid.Empty
                : OrganizationId;
        }
    }

    private void Subscribe(
        EventHandler<GeoFeature>? handler,
        ConcurrentDictionary<EventHandler<GeoFeature>, EventHandler<GeoFeature>> wrappers,
        Action<EventHandler<GeoFeature>> attach)
    {
        if (handler is null) return;

        var wrapper = wrappers.GetOrAdd(handler, h => (sender, feature) =>
        {
            if (IsVisible(feature)) h(sender, feature);
        });
        attach(wrapper);
    }

    private static void Unsubscribe(
        EventHandler<GeoFeature>? handler,
        ConcurrentDictionary<EventHandler<GeoFeature>, EventHandler<GeoFeature>> wrappers,
        Action<EventHandler<GeoFeature>> detach)
    {
        if (handler is not null && wrappers.TryRemove(handler, out var wrapper)) detach(wrapper);
    }
}
