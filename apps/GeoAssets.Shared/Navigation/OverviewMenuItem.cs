using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>Top-level link to the map page (XD01-85) — today's first, always-visible nav item.</summary>
public sealed class OverviewMenuItem : MenuPageItem
{
    public override string Id => "overview";
    public override string LabelKey => "nav.overview";
    public override string? Icon => "🗺️";
    public override int SortOrder => 0;
    public override string RouteHref => "";
    public override MenuLinkMatch Match => MenuLinkMatch.Exact;
}
