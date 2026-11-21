using GeoAssets.Core.Models;
using GeoAssets.Integrations.Scada;
using GeoAssets.FieldService.Agents.Dispatch;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// In-memory <see cref="IAssetTopologyQuery"/> and <see cref="IDispatchAssetQuery"/> for host
/// tests (XD01-386): a feature store with canned descendants, each read failable on demand to
/// simulate the Core GeoAssets Server being unreachable.
/// </summary>
internal sealed class FakeAssetTopologyQuery : IAssetTopologyQuery, IDispatchAssetQuery
{
    public Dictionary<string, GeoFeature> Features { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<GeoFeature>> Descendants { get; } = new(StringComparer.Ordinal);

    public Exception? FeatureFailure { get; set; }

    public Exception? DescendantsFailure { get; set; }

    public Exception? ByAssetTypeFailure { get; set; }

    public Task<GeoFeature?> GetFeatureAsync(string id, CancellationToken ct) =>
        FeatureFailure is { } failure
            ? Task.FromException<GeoFeature?>(failure)
            : Task.FromResult(Features.GetValueOrDefault(id));

    public Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(string id, string edgeKind, CancellationToken ct) =>
        DescendantsFailure is { } failure
            ? Task.FromException<IReadOnlyList<GeoFeature>>(failure)
            : Task.FromResult<IReadOnlyList<GeoFeature>>(Descendants.TryGetValue(id, out var list) ? list : []);

    public Task<IReadOnlyList<GeoFeature>> GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct) =>
        ByAssetTypeFailure is { } failure
            ? Task.FromException<IReadOnlyList<GeoFeature>>(failure)
            : Task.FromResult<IReadOnlyList<GeoFeature>>(
                [.. Features.Values.Where(f => string.Equals(f.Properties.AssetTypeId, assetTypeId, StringComparison.Ordinal))]);
}
