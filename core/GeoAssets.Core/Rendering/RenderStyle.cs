using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// The color a feature is drawn with (XD01-372): its <see cref="LayerResolver"/> layer's color, else its asset type's
/// color, else <see cref="DefaultColor"/>. One resolution for every <see cref="IAssetRenderQuery"/>, so a pushdown and
/// the generic query paint the same feature the same way.
/// </summary>
public sealed class RenderStyle
{
    /// <summary>The color of a feature with neither a resolved layer nor a known asset type.</summary>
    public const string DefaultColor = "#3388ff";

    private readonly Dictionary<string, AssetType> _assetTypes;
    private readonly IReadOnlyList<Layer> _layers;
    private readonly IReadOnlyList<LayerRule> _layerRules;

    public RenderStyle(IEnumerable<AssetType> assetTypes, IReadOnlyList<Layer> layers, IReadOnlyList<LayerRule> layerRules)
    {
        ArgumentNullException.ThrowIfNull(assetTypes);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerRules);

        _assetTypes = assetTypes.ToDictionary(t => t.Id.ToString(), StringComparer.OrdinalIgnoreCase);
        _layers = layers;
        _layerRules = layerRules;
    }

    /// <summary>The style of <paramref name="catalog"/>: its asset types, layers and every asset type's layer rules.</summary>
    public static RenderStyle Of(Interfaces.IAssetProvider catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var assetTypes = catalog.GetAssetTypes();
        return new RenderStyle(assetTypes, catalog.GetLayers(), [.. assetTypes.SelectMany(t => catalog.GetLayerRules(t.Id))]);
    }

    public string ColorOf(GeoFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var assetType = _assetTypes.GetValueOrDefault(feature.Properties.AssetTypeId);
        return LayerResolver.Resolve(feature, assetType, _layers, _layerRules)?.Color
            ?? assetType?.Color
            ?? DefaultColor;
    }
}
