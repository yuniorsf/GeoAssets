using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.IpBilling;

/// <summary>
/// Renders a "troca" (swap) pill when <see cref="IsSwap"/> is true, nothing otherwise — a dumb,
/// parameter-down rendering of whatever <c>OficioLineSwapView.Compute</c> (XD01-185) already
/// derived for a given line. Which line's flag to pass in is resolved by the caller
/// (<see cref="OficioDetail.IsLineSwapped"/>), not computed here.
/// </summary>
public partial class OficioSwapBadge
{
    [Parameter] public bool IsSwap { get; set; }
}
