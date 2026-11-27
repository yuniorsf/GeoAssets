namespace GeoAssets.Shared.Services.Rendering;

/// <summary>The precedences of <see cref="IProviderMapLayerRenderer.Precedence"/> (XD01-436).</summary>
public static class MapLayerRendererPrecedence
{
    /// <summary>A renderer for its own kind of provider (<see cref="WmsMapLayerRenderer"/>,
    /// <see cref="VectorFeatureMapLayerRenderer"/>).</summary>
    public const int Default = 0;

    /// <summary>A renderer that draws some providers another one also accepts differently
    /// (<see cref="HybridLodMapLayerRenderer"/>): asked before every <see cref="Default"/> one.</summary>
    public const int Refining = 100;
}
