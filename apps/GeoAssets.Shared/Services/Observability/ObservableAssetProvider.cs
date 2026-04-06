using System.Text.Json;
using GeoAssets.Shared.Services.Observability;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.Observability;

/// <summary>
/// Observable decorator for <see cref="IAssetProvider"/>.
/// Instruments <see cref="GetAll"/> with an OpenTelemetry span,
/// a duration metric, and structured logging; all other members are
/// forwarded to the inner repository without overhead.
/// </summary>
public sealed class ObservableAssetProvider(
    IAssetProvider inner,
    ILogger<ObservableAssetProvider> logger)
    : ObservableDecoratorBase<ObservableAssetProvider>(logger), IAssetProvider
{
    // ── Instrumented ─────────────────────────────────────────────────────────

    public IReadOnlyList<GeoFeature> GetAll() =>
        TrackSync(
            "repository.get_all",
            inner.GetAll,
            after: (span, result, elapsedMs) =>
            {
                span?.SetTag("feature.count", result.Count);
                ImportDiagnostics.GetAllDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "Repository.GetAll — {FeatureCount} features in {ElapsedMs} ms",
                    result.Count, elapsedMs);
            });

    public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        TrackAsync(
            "repository.get_in_bounds",
            async () => await inner.GetInBoundsAsync(minLon, minLat, maxLon, maxLat),
            after: (span, result, elapsedMs) =>
            {
                span?.SetTag("feature.count", result.Count);
                ImportDiagnostics.GetInBoundsDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "Repository.GetInBounds — {FeatureCount} features in {ElapsedMs} ms for bounds [{MinLon}, {MinLat}, {MaxLon}, {MaxLat} ] (provider: {ProviderType})",
                    result.Count, elapsedMs, minLon, minLat, maxLon, maxLat, inner.GetType().FullName);
            });

    public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) =>
        TrackAsync(
            "repository.get_in_bounds_json",
            async () => await inner.GetInBoundsJsonAsync(minLon, minLat, maxLon, maxLat),
            after: (span, result, elapsedMs) =>
            {
                span?.SetTag("feature.count", result.Count);
                ImportDiagnostics.GetInBoundsDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "Repository.GetInBoundsJson — {FeatureCount} features in {ElapsedMs} ms for bounds [{MinLon}, {MinLat}, {MaxLon}, {MaxLat}] (provider: {ProviderType})",
                    result.Count, elapsedMs, minLon, minLat, maxLon, maxLat, inner.GetType().FullName);
            });

    // ── Pass-through: reads ───────────────────────────────────────────────────

    public GeoFeature?                              GetById(string id)                                    => inner.GetById(id);
    public IReadOnlyList<GeoFeature>                GetByAssetType(string assetTypeId)                   => inner.GetByAssetType(assetTypeId);
    public IReadOnlyList<GeoFeature>                Search(string query)                                  => inner.Search(query);
    public IReadOnlyList<GeoFeature>                GetWithin(GeoGeometry bounds)                        => inner.GetWithin(bounds);
    public IReadOnlyList<GeoFeature>                GetIntersecting(GeoGeometry geometry)                => inner.GetIntersecting(geometry);
public IReadOnlyList<GeoFeature>                GetNearby(GeoPoint center, double distanceDegrees)   => inner.GetNearby(center, distanceDegrees);
    public IReadOnlyList<GeoFeature>                GetNeighbors(string featureId)                       => inner.GetNeighbors(featureId);
    public IReadOnlyList<GeoFeature>                GetDescendants(string featureId)                     => inner.GetDescendants(featureId);
    public IReadOnlyList<GeoFeature>                GetAncestors(string featureId)                       => inner.GetAncestors(featureId);
    public IReadOnlyList<GeoFeature>                FindPath(string fromId, string toId)                 => inner.FindPath(fromId, toId);
    public IReadOnlyList<GeoFeature>                FindShortestPath(string fromId, string toId)         => inner.FindShortestPath(fromId, toId);
    public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents()                             => inner.GetConnectedComponents();
    public bool                                     HasCycles()                                          => inner.HasCycles();
    public IReadOnlyList<GeoFeature>                TopologicalSort()                                    => inner.TopologicalSort();
    public IReadOnlyList<AssetType>                 GetAssetTypes()                                      => inner.GetAssetTypes();

    // ── Pass-through: writes ──────────────────────────────────────────────────

    public void Add(GeoFeature feature)                => inner.Add(feature);
    public void Update(GeoFeature feature)             => inner.Update(feature);
    public void AddRange(IEnumerable<GeoFeature> features) => inner.AddRange(features);
    public void Delete(string id)                      => inner.Delete(id);
    public void Clear()                                => inner.Clear();
    public void LoadAll(IEnumerable<GeoFeature> features) => inner.LoadAll(features);
    public void AddAssetType(AssetType assetType)      => inner.AddAssetType(assetType);
    public void DeleteAssetType(Guid id)               => inner.DeleteAssetType(id);

    // ── Event forwarding ──────────────────────────────────────────────────────

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
