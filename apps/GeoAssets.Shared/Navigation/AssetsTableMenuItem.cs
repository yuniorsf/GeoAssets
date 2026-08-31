using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>
/// Top-level link to the paginated Assets table page (XD01-116) — distinct from
/// <see cref="AssetListMenuItem"/>, which opens the sidebar panel used for map-context
/// browsing/selection. This one is a full-width routed page for browsing/filtering large
/// asset collections server-side via <c>IAssetProvider.GetPageAsync</c>.
/// </summary>
public sealed class AssetsTableMenuItem : MenuPageItem
{
    public override string Id => "assets-table";
    public override string LabelKey => "assetsTable.title";
    public override string? Icon => "📋";
    public override int SortOrder => 35;
    public override string RouteHref => "assets/table";
}
