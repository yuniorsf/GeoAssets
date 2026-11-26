using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Profiles;

namespace GeoAssets.Ingestion.Tests.Fakes;

internal sealed class FakeSourceProfile(
    IReadOnlyList<LayerDefinition> layers,
    IReadOnlyList<AssetClassDefinition> assetClasses,
    ILayerMapper mapper,
    IReadOnlyDictionary<string, string>? fallbackAliases = null) : ISourceProfile
{
    public string Id => "test-profile";

    public IReadOnlyList<LayerDefinition> Layers { get; } = layers;

    public IReadOnlyList<AssetClassDefinition> AssetClasses { get; } = assetClasses;

    public IReadOnlyDictionary<string, string> FallbackFieldAliases(string layer) =>
        fallbackAliases ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public ILayerMapper MapperFor(string layer) => mapper;
}
