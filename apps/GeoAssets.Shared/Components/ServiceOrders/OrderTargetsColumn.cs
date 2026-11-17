namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>A sortable column of <see cref="OrderTargetsTable"/> (XD01-312).</summary>
/// <param name="SortKey">The <c>OrderTargetSortKeys</c> key the column sorts by.</param>
/// <param name="LabelKey">Localization key of the header.</param>
public sealed record OrderTargetsColumn(string SortKey, string LabelKey);
