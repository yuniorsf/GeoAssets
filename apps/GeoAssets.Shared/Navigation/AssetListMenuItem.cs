using GeoAssets.Core.Navigation;
using GeoAssets.Shared.Components.Assets;

namespace GeoAssets.Shared.Navigation;

/// <summary>
/// Opens the <see cref="AssetList"/> panel (XD01-85). Its Id ("assets") is also NavMenu's
/// default open panel — matches Index.razor's old <c>SidebarPanel.AssetList</c> default so the
/// panel starts open on first load, same as before this ticket.
/// </summary>
public sealed class AssetListMenuItem : MenuPanelItem
{
    public override string Id => "assets";
    public override string LabelKey => "assets.title";
    public override string? Icon => "📦";
    public override int SortOrder => 30;
    public override Type ComponentType => typeof(AssetList);
}
