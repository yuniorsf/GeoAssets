using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Pipeline;

/// <summary>Counters of a run: projected elements per asset class, resolved references per method, unresolved ones.</summary>
internal sealed class ImportStatistics
{
    private readonly Dictionary<string, long> _elementsByAssetClass = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _referencesByMethod = new(StringComparer.Ordinal)
    {
        [ResolutionMethodLabels.Key] = 0,
        [ResolutionMethodLabels.Snap] = 0,
        [ResolutionMethodLabels.Anchor] = 0,
    };

    public IReadOnlyDictionary<string, long> ElementsByAssetClass => _elementsByAssetClass;

    public IReadOnlyDictionary<string, long> ReferencesByMethod => _referencesByMethod;

    public long UnresolvedReferences { get; private set; }

    public void CountElement(string assetClass) =>
        _elementsByAssetClass[assetClass] = _elementsByAssetClass.GetValueOrDefault(assetClass) + 1;

    public void CountResolved(ResolutionMethod method)
    {
        var label = ResolutionMethodLabels.For(method);
        _referencesByMethod[label] = _referencesByMethod.GetValueOrDefault(label) + 1;
    }

    public void CountUnresolved() => UnresolvedReferences++;
}
