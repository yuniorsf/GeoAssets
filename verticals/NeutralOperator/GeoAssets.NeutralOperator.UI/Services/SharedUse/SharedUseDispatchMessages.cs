using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.UI.Services.SharedUse;

/// <summary>
/// Which localized string the shared-use dispatch dialog shows for each thing the dispatch
/// service can tell it (XD01-316). Kept apart from the component so that every reason, skip
/// reason and failure is checked against the language files by a test.
/// </summary>
public static class SharedUseDispatchMessages
{
    /// <summary>The key explaining why an order cannot be dispatched as it stands.</summary>
    public static string BlockKey(SharedUseDispatchBlockReason reason) => reason switch
    {
        SharedUseDispatchBlockReason.NoTargets => "sharedUse.block.noTargets",
        SharedUseDispatchBlockReason.TargetsWithoutActivity => "sharedUse.block.targetsWithoutActivity",
        SharedUseDispatchBlockReason.AllTargetsBlocked => "sharedUse.block.allTargetsBlocked",
        SharedUseDispatchBlockReason.NoEligibleOccupants => "sharedUse.block.noEligibleOccupants",
        _ => "sharedUse.block.other",
    };

    /// <summary>The key saying why an attachment gets no order.</summary>
    public static string SkipReasonKey(SharedUseSkipReason reason) => reason switch
    {
        SharedUseSkipReason.NoActiveContract => "sharedUse.skipReason.noActiveContract",
        SharedUseSkipReason.UnknownOrganization => "sharedUse.skipReason.unknownOrganization",
        SharedUseSkipReason.PoleOwner => "sharedUse.skipReason.poleOwner",
        _ => "sharedUse.skipReason.other",
    };

    /// <summary>The key for a failure of the dispatch service, most specific first.</summary>
    public static string ErrorKey(Exception exception) => exception switch
    {
        OrderActionDeniedException => "sharedUse.error.denied",
        ServiceOrderConcurrencyException => "sharedUse.error.changed",
        InvalidServiceOrderTransitionException => "sharedUse.error.invalidState",
        SharedUseDispatchBlockedException blocked => BlockKey(blocked.Reason),
        OrderTypeHasNoFanOutException => "sharedUse.error.notShared",
        KeyNotFoundException => "sharedUse.error.notFound",
        _ => "sharedUse.error.unexpected",
    };

    /// <summary>Every key the three methods above can return.</summary>
    public static IReadOnlyList<string> MessageKeys { get; } =
    [
        "sharedUse.block.noTargets", "sharedUse.block.targetsWithoutActivity", "sharedUse.block.allTargetsBlocked",
        "sharedUse.block.noEligibleOccupants", "sharedUse.block.other",
        "sharedUse.skipReason.noActiveContract", "sharedUse.skipReason.unknownOrganization", "sharedUse.skipReason.poleOwner",
        "sharedUse.skipReason.other",
        "sharedUse.error.denied", "sharedUse.error.changed", "sharedUse.error.invalidState", "sharedUse.error.notShared",
        "sharedUse.error.notFound", "sharedUse.error.unexpected",
    ];
}
