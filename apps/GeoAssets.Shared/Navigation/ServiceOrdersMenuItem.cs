using GeoAssets.Core.Navigation;

namespace GeoAssets.Shared.Navigation;

/// <summary>Top-level link to the Service Orders page (XD01-85).</summary>
public sealed class ServiceOrdersMenuItem : MenuPageItem
{
    public override string Id => "service-orders";
    public override string LabelKey => "orders.title";
    public override string? Icon => "🗂️";
    public override int SortOrder => 40;
    public override string RouteHref => "service-orders";
}
