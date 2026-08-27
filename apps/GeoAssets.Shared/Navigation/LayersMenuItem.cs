using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.Layers;

namespace GeoAssets.Shared.Navigation;

/// <summary>Opens the <see cref="LayerManager"/> panel (XD01-85).</summary>
public sealed class LayersMenuItem : MenuPanelItem
{
    public override string Id => "layers";
    public override string LabelKey => "map.layers";
    public override string? Icon => "🧱";
    public override int SortOrder => 20;
    public override Type ComponentType => typeof(LayerManager);
}
