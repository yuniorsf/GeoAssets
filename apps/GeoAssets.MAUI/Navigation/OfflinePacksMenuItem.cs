using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.OfflinePacks;

namespace GeoAssets.MAUI.Navigation;

/// <summary>
/// Opens the <see cref="OfflinePacksPanel"/> ("Offline maps", XD01-464) from the left nav, after Collections. Lives in the
/// MAUI assembly because only this host stores offline map packs (<c>AddPMTilesPackStorage</c> registers the catalog the
/// panel reads); the Web discovers no such item.
/// </summary>
internal sealed class OfflinePacksMenuItem : MenuPanelItem
{
    public override string Id => "offline-packs";
    public override string LabelKey => "offlinePacks.title";
    public override string? Icon => "💾";
    public override int SortOrder => 55;
    public override Type ComponentType => typeof(OfflinePacksPanel);
}
