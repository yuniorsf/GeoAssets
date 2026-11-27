using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// The <c>plan</c> sink: every feature appended to an NDJSON stream; asset types are the classes' own ids. It also
/// notes every attribute key the features carry (<see cref="AttributeKeys"/>, first-seen order), so <c>--export</c>
/// can declare its columns and write the file in one pass, and the asset class of each asset type id
/// (<see cref="AssetClassNames"/>), which names <c>--export pmtiles</c>'s layers.
/// </summary>
internal sealed class NdjsonFileFeatureSink(Stream output) : IFeatureSink
{
    private readonly Dictionary<string, string> _assetTypeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _assetClassNames = new(StringComparer.Ordinal);
    private readonly List<string> _attributeKeys = [];
    private readonly HashSet<string> _seenAttributeKeys = new(StringComparer.Ordinal);

    /// <summary>Every attribute key of the features written so far, in first-seen order.</summary>
    public IReadOnlyList<string> AttributeKeys => _attributeKeys;

    /// <summary>The name of each ensured asset type id's asset class (the first class, should two share an id).</summary>
    public IReadOnlyDictionary<string, string> AssetClassNames => _assetClassNames;

    public Task EnsureAssetTypesAsync(IReadOnlyList<AssetClassDefinition> classes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classes);
        foreach (var assetClass in classes)
        {
            _assetTypeIds[assetClass.Name] = assetClass.TypeId.ToString();
            _assetClassNames.TryAdd(assetClass.TypeId.ToString(), assetClass.Name);
        }

        return Task.CompletedTask;
    }

    public string AssetTypeIdFor(string assetClass) =>
        _assetTypeIds.TryGetValue(assetClass, out var id)
            ? id
            : throw new InvalidOperationException($"No asset type was ensured for the asset class '{assetClass}'.");

    public async Task<SinkChunkResult> WriteChunkAsync(int sequence, IReadOnlyList<GeoFeature> features, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(features);
        foreach (var key in features.SelectMany(feature => feature.Properties.CustomAttributes.Keys))
        {
            if (_seenAttributeKeys.Add(key))
                _attributeKeys.Add(key);
        }

        await NdjsonEncoding.WriteAsync(output, features, cancellationToken).ConfigureAwait(false);
        return new SinkChunkResult(features.Count, []);
    }

    public Task CompleteAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
}
