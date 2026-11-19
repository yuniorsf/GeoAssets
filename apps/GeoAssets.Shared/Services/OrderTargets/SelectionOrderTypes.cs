using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The policy-aware order type picker's decision (XD01-312): which order types can take the map
/// selection as their targets.
/// </summary>
public static class SelectionOrderTypes
{
    /// <summary>
    /// The order types of <paramref name="creatableTypes"/> (already restricted to those the user
    /// may create) that can take the selection: the type declares a target policy, and at least one
    /// selected feature is of an asset type the policy targets — or is not loaded on the client, in
    /// which case the server decides. A type whose policy targets none of the selected features'
    /// types is left out. Order is kept.
    /// </summary>
    /// <param name="creatableTypes">Candidate order types, in display order.</param>
    /// <param name="selectedIds">The selected feature ids.</param>
    /// <param name="assetTypeOf">The asset type id of a loaded feature, or null when the client has no such feature.</param>
    /// <param name="assetTypes">The loaded asset types, which resolve a policy's targeted roles (XD01-368).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<SelectionOrderTypeFit> Evaluate(
        IEnumerable<OrderType> creatableTypes,
        IReadOnlyCollection<string> selectedIds,
        Func<string, string?> assetTypeOf,
        IReadOnlyCollection<AssetType> assetTypes)
    {
        ArgumentNullException.ThrowIfNull(creatableTypes);
        ArgumentNullException.ThrowIfNull(selectedIds);
        ArgumentNullException.ThrowIfNull(assetTypeOf);
        ArgumentNullException.ThrowIfNull(assetTypes);

        var typeOfSelected = selectedIds.Select(assetTypeOf).ToList();

        var fits = new List<SelectionOrderTypeFit>();
        foreach (var type in creatableTypes)
        {
            if (type.TargetPolicy is not { } policy) continue;

            var targeted = OrderTargetAssetTypes.Resolve(policy, assetTypes).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknown = typeOfSelected.Count(t => t is null);
            var eligible = typeOfSelected.Count(t => t is not null && targeted.Contains(t));
            var other = typeOfSelected.Count - unknown - eligible;

            if (eligible + unknown > 0)
                fits.Add(new SelectionOrderTypeFit(type, eligible, other, unknown));
        }

        return fits;
    }

    /// <summary>
    /// Whether the create form starts with the selection as the new order's targets: only when
    /// the map asked for it and something is selected.
    /// </summary>
    public static bool UseSelectionInitially(bool fromSelection, int selectedCount) => fromSelection && selectedCount > 0;

    /// <summary>
    /// The order type to have chosen: <paramref name="currentId"/> while it is still offered, the
    /// first offered type otherwise, or empty when nothing is offered.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="offered"/> is null.</exception>
    public static string KeepOrFirst(string? currentId, IReadOnlyList<OrderType> offered)
    {
        ArgumentNullException.ThrowIfNull(offered);

        if (offered.Any(t => string.Equals(t.Id, currentId, StringComparison.Ordinal))) return currentId!;

        return offered.Count > 0 ? offered[0].Id : string.Empty;
    }
}
