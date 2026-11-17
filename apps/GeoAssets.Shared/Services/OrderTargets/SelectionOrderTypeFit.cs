using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// How well one order type fits the map selection (XD01-312), judged from the features the
/// client has loaded. The server re-resolves the selection for the type's asset types, so these
/// counts are a guide for the picker, not a promise.
/// </summary>
/// <param name="Type">The order type; it always has a <see cref="OrderType.TargetPolicy"/>.</param>
/// <param name="EligibleCount">Selected features whose asset type the policy targets.</param>
/// <param name="OtherTypeCount">Selected features of an asset type the policy does not target.</param>
/// <param name="UnknownCount">Selected ids the client has no feature for; the server decides about them.</param>
public sealed record SelectionOrderTypeFit(OrderType Type, int EligibleCount, int OtherTypeCount, int UnknownCount)
{
    /// <summary>The order type's selection limit, if it has one.</summary>
    public int? SelectionLimit => Type.TargetPolicy?.SelectionLimit;

    /// <summary>
    /// Whether more features may become targets than the type's selection limit allows, so the
    /// preview will report a truncation. Unknown ids count, as they may turn out eligible.
    /// </summary>
    public bool MayExceedLimit => SelectionLimit is { } limit && EligibleCount + UnknownCount > limit;
}
