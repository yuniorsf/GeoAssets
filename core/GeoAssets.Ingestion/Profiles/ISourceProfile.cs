using GeoAssets.Ingestion.Model;

namespace GeoAssets.Ingestion.Profiles;

/// <summary>
/// A delivery format (e.g. <c>neoenergia-gse</c>): which layers to read, the asset classes elements map to, and a
/// mapper per layer. A new distributor format is a new profile; the pipeline does not change.
/// </summary>
public interface ISourceProfile
{
    string Id { get; }

    IReadOnlyList<LayerDefinition> Layers { get; }

    IReadOnlyList<AssetClassDefinition> AssetClasses { get; }

    /// <summary>Full field names of a layer keyed by the truncated DBF names, for deliveries without lineage metadata.</summary>
    IReadOnlyDictionary<string, string> FallbackFieldAliases(string layer);

    ILayerMapper MapperFor(string layer);
}
