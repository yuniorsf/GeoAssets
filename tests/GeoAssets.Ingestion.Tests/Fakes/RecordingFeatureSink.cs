using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class RecordingFeatureSink : IFeatureSink
{
    public List<string> Events { get; } = [];

    public List<AssetClassDefinition> EnsuredClasses { get; } = [];

    public List<(int Sequence, IReadOnlyList<GeoFeature> Features)> Chunks { get; } = [];

    public Func<int, IReadOnlyList<GeoFeature>, IReadOnlyList<ImportIssue>>? Rejector { get; set; }

    public IEnumerable<GeoFeature> Features => Chunks.SelectMany(chunk => chunk.Features);

    public Task EnsureAssetTypesAsync(IReadOnlyList<AssetClassDefinition> classes, CancellationToken cancellationToken)
    {
        Events.Add("ensure");
        EnsuredClasses.AddRange(classes);
        return Task.CompletedTask;
    }

    public string AssetTypeIdFor(string assetClass) => $"type-{assetClass}";

    public Task<SinkChunkResult> WriteChunkAsync(int sequence, IReadOnlyList<GeoFeature> features, CancellationToken cancellationToken)
    {
        Events.Add($"write:{sequence}");
        Chunks.Add((sequence, features.ToArray()));
        var rejections = Rejector?.Invoke(sequence, features) ?? [];
        return Task.FromResult(new SinkChunkResult(features.Count - rejections.Count, rejections));
    }

    public Task CompleteAsync(CancellationToken cancellationToken)
    {
        Events.Add("complete");
        return Task.CompletedTask;
    }
}
