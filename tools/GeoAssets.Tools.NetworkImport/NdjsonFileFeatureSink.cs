using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>The <c>plan</c> sink: every feature appended to an NDJSON stream; asset types are the classes' own ids.</summary>
internal sealed class NdjsonFileFeatureSink(Stream output) : IFeatureSink
{
    private readonly Dictionary<string, string> _assetTypeIds = new(StringComparer.Ordinal);

    public Task EnsureAssetTypesAsync(IReadOnlyList<AssetClassDefinition> classes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classes);
        foreach (var assetClass in classes)
            _assetTypeIds[assetClass.Name] = assetClass.TypeId.ToString();
        return Task.CompletedTask;
    }

    public string AssetTypeIdFor(string assetClass) =>
        _assetTypeIds.TryGetValue(assetClass, out var id)
            ? id
            : throw new InvalidOperationException($"No asset type was ensured for the asset class '{assetClass}'.");

    public async Task<SinkChunkResult> WriteChunkAsync(int sequence, IReadOnlyList<GeoFeature> features, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(features);
        await NdjsonEncoding.WriteAsync(output, features, cancellationToken).ConfigureAwait(false);
        return new SinkChunkResult(features.Count, []);
    }

    public Task CompleteAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
}
