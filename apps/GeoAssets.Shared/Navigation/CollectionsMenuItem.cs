using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.Providers;

namespace GeoAssets.Shared.Navigation;

/// <summary>Opens the <see cref="ProviderPoolPanel"/> ("Collections") panel (XD01-85).</summary>
public sealed class CollectionsMenuItem : MenuPanelItem
{
    public override string Id => "collections";
    public override string LabelKey => "pool.title";
    public override string? Icon => "🔌";
    public override int SortOrder => 50;
    public override Type ComponentType => typeof(ProviderPoolPanel);
}
