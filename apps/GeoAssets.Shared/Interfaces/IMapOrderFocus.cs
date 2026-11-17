using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Points the current map (<see cref="ICurrentMapContext"/>) at a service order (XD01-311):
/// fits the view to the order's targets and outlines the fence its selection was made with.
/// The order panels call this; it draws no UI of its own. Every member returns whether the map
/// was actually changed, and none of them throws for "nothing to show".
/// </summary>
public interface IMapOrderFocus
{
    /// <summary>
    /// Fits the map to <paramref name="extent"/>. A null (or non-finite) extent is a no-op that
    /// returns false; a single-point or zero-width/height extent is padded first
    /// (<see cref="Services.MapExtentFitter"/>).
    /// </summary>
    Task<bool> FitToExtentAsync(OrderTargetExtent? extent);

    /// <summary>
    /// Reads the extent of <paramref name="orderId"/>'s positioned targets and fits the map to
    /// it. Returns false, leaving the view alone, when the order has no positioned target.
    /// </summary>
    Task<bool> FitToOrderAsync(string orderId, CancellationToken ct);

    /// <summary>
    /// Outlines the fence recorded in <paramref name="spec"/> (strategy <c>polygon-fence</c>).
    /// Any outline already shown is removed first, so a spec without a drawable fence — null,
    /// another strategy, or malformed rings (logged) — leaves the map with no outline and
    /// returns false.
    /// </summary>
    Task<bool> ShowFenceOutlineAsync(FeatureSelectionSpec? spec);

    /// <summary>
    /// Loads <paramref name="orderId"/> and outlines the fence of its selection, as
    /// <see cref="ShowFenceOutlineAsync"/> does. An unknown order clears the outline and returns false.
    /// </summary>
    Task<bool> ShowOrderFenceOutlineAsync(string orderId, CancellationToken ct);

    /// <summary>Removes the fence outline, if one is shown.</summary>
    Task ClearFenceOutlineAsync();
}
