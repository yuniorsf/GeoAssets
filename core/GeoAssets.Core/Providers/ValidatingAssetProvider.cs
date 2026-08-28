using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Providers;

/// <summary>
/// Decorates any <see cref="IAssetProvider"/> implementation with
/// <see cref="GeoFeatureAttributeValidator"/> enforcement on <see cref="Add"/> and
/// <see cref="Update"/>, validating a feature's <see cref="GeoFeatureProperties.CustomAttributes"/>
/// against its <see cref="AssetType.AttributesSchemaJson"/> before delegating.
///
/// Generalizes <c>GeoAssets.Workflow.Orders.ValidatingServiceOrderRepository</c> (XD01-2) to
/// <see cref="IAssetProvider"/>. Unlike <c>OrderType</c>, which needs a separate
/// <c>OrderTypeRegistry</c>, asset types are already part of <see cref="IAssetProvider"/>'s own
/// state (<see cref="IAssetProvider.GetAssetTypes"/>), so no external registry is needed —
/// the decorator looks the type up on <paramref name="inner"/> directly.
///
/// A feature whose <see cref="GeoFeatureProperties.AssetTypeId"/> doesn't match any known
/// <see cref="AssetType"/> is unrestricted (same "unrestricted by default" convention as the
/// ServiceOrder-side decorator when an order type isn't registered).
/// </summary>
public sealed class ValidatingAssetProvider(IAssetProvider inner) : IAssetProvider, IAsyncDisposable
{
    /// <summary>Forwards disposal to <paramref name="inner"/> when it owns disposable resources
    /// (e.g. <c>PostgresAssetProvider</c>'s <c>DbContext</c>) — without this, wrapping a
    /// disposable provider would silently stop it from being disposed by the DI container.</summary>
    public async ValueTask DisposeAsync()
    {
        switch (inner)
        {
            case IAsyncDisposable asyncDisposable: await asyncDisposable.DisposeAsync(); break;
            case IDisposable disposable: disposable.Dispose(); break;
        }
    }

    // ── Read (pass-through) ─────────────────────────────────────────────────────

    public GeoFeature?                              GetById(string id)                                  => inner.GetById(id);
    public IReadOnlyList<GeoFeature>                GetAll()                                            => inner.GetAll();
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)                 => inner.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature>                Search(string query)                                => inner.Search(query);
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                      => inner.GetWithin(bounds);
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)              => inner.GetIntersecting(geometry);
    public Task<IReadOnlyList<GeoFeature>>          GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat)        => inner.GetInBoundsAsync(minLon, minLat, maxLon, maxLat);
    public Task<IReadOnlyList<JsonElement>>         GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat)    => inner.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat);
    public Task<string?>                            GetInBoundsRawJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => inner.GetInBoundsRawJsonAsync(minLon, minLat, maxLon, maxLat);
    public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDegrees) => inner.GetNearby(center, distanceDegrees);
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                     => inner.GetNeighbors(featureId);
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)                   => inner.GetDescendants(featureId);
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                     => inner.GetAncestors(featureId);
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)               => inner.FindPath(fromId, toId);
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)       => inner.FindShortestPath(fromId, toId);
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                           => inner.GetConnectedComponents();
    public bool                                     HasCycles()                                        => inner.HasCycles();
    public IReadOnlyList<GeoFeature>                TopologicalSort()                                  => inner.TopologicalSort();
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                    => inner.GetAssetTypes();
    public IReadOnlyList<Layer>                     GetLayers()                                        => inner.GetLayers();
    public IReadOnlyList<LayerRule>                 GetLayerRules(Guid assetTypeId)                    => inner.GetLayerRules(assetTypeId);

    // ── Write ────────────────────────────────────────────────────────────────────

    public void Add(GeoFeature feature)
    {
        ValidateAttributes(feature);
        ValidateGeometry(feature);
        inner.Add(feature);
    }

    public void Update(GeoFeature feature)
    {
        ValidateAttributes(feature);
        ValidateGeometry(feature);
        inner.Update(feature);
    }

    /// <summary>
    /// No-op when <paramref name="feature"/>'s <see cref="GeoFeatureProperties.AssetTypeId"/>
    /// doesn't match a known <see cref="AssetType"/>, or that type has no
    /// <see cref="AssetType.AttributesSchemaJson"/> — same "unrestricted by default" behavior
    /// as <c>ValidatingServiceOrderRepository</c>'s optional registry lookup.
    /// </summary>
    private void ValidateAttributes(GeoFeature feature)
    {
        var assetType = inner.GetAssetTypes()
            .FirstOrDefault(t => t.Id.ToString() == feature.Properties.AssetTypeId);
        if (assetType is not null)
            GeoFeatureAttributeValidator.EnsureValid(assetType, feature.Properties.CustomAttributes);
    }

    /// <summary>
    /// No-op when the asset type is unknown, its <see cref="AssetType.AllowedGeometryType"/> is
    /// unrestricted (<c>null</c>), or the feature has no geometry yet — same "unrestricted by
    /// default" convention as <see cref="ValidateAttributes"/>.
    /// </summary>
    private void ValidateGeometry(GeoFeature feature)
    {
        var assetType = inner.GetAssetTypes()
            .FirstOrDefault(t => t.Id.ToString() == feature.Properties.AssetTypeId);
        if (assetType?.AllowedGeometryType is not { } allowed || feature.Geometry is null)
            return;

        if (feature.Geometry.GeometryType != allowed)
            throw new GeoFeatureGeometryMismatchException(assetType.Id, allowed, feature.Geometry.GeometryType);
    }

    public void AddRange(IEnumerable<GeoFeature> features) => inner.AddRange(features);
    public void Delete(string id)                          => inner.Delete(id);
    public void Clear()                                    => inner.Clear();
    public void LoadAll(IEnumerable<GeoFeature> features)  => inner.LoadAll(features);
    public void AddAssetType(AssetType assetType)          => inner.AddAssetType(assetType);
    public void DeleteAssetType(Guid id)                   => inner.DeleteAssetType(id);
    public void AddLayer(Layer layer)                      => inner.AddLayer(layer);
    public void DeleteLayer(Guid id)                       => inner.DeleteLayer(id);
    public void AddLayerRule(LayerRule layerRule)          => inner.AddLayerRule(layerRule);
    public void DeleteLayerRule(Guid id)                   => inner.DeleteLayerRule(id);

    // ── Events (forwarded) ─────────────────────────────────────────────────────

    public event EventHandler<GeoFeature>? FeatureAdded
    {
        add    => inner.FeatureAdded += value;
        remove => inner.FeatureAdded -= value;
    }

    public event EventHandler<GeoFeature>? FeatureUpdated
    {
        add    => inner.FeatureUpdated += value;
        remove => inner.FeatureUpdated -= value;
    }

    public event EventHandler<string>? FeatureDeleted
    {
        add    => inner.FeatureDeleted += value;
        remove => inner.FeatureDeleted -= value;
    }

    public event EventHandler? CollectionChanged
    {
        add    => inner.CollectionChanged += value;
        remove => inner.CollectionChanged -= value;
    }
}
