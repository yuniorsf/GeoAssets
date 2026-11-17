using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The localization keys of the targets grid and the create-from-selection flow (XD01-312), and
/// the badge styling of a conflict level. Kept out of the components so every branch is testable.
/// </summary>
public static class OrderTargetMessages
{
    private const string StatusPrefix = "orders.targets.status.";
    private const string UnknownStatusKey = "orders.targets.status.unknown";

    /// <summary>The label key of a target status; an undefined status gets a generic label.</summary>
    public static string StatusKey(string? status) =>
        OrderTargetStatus.IsDefined(status) ? StatusPrefix + status : UnknownStatusKey;

    /// <summary>The label key of a conflict level; an undefined value reads as no conflict.</summary>
    public static string ConflictKey(OrderTargetConflictLevel level) => level switch
    {
        OrderTargetConflictLevel.Warning => "orders.targets.conflict.warning",
        OrderTargetConflictLevel.Blocked => "orders.targets.conflict.blocked",
        _ => "orders.targets.conflict.none",
    };

    /// <summary>
    /// The CSS class of a conflict badge, or null for no badge (no conflict). The badge always
    /// carries its text too, so the colour is never the only signal.
    /// </summary>
    public static string? ConflictBadgeClass(OrderTargetConflictLevel level) => level switch
    {
        OrderTargetConflictLevel.Warning => "order-target-badge order-target-badge-warning",
        OrderTargetConflictLevel.Blocked => "order-target-badge order-target-badge-danger",
        _ => null,
    };

    /// <summary>The message key explaining why the selection cannot become the order's targets.</summary>
    public static string PlanProblemKey(OrderTargetSelectionPlanProblem problem) => problem switch
    {
        OrderTargetSelectionPlanProblem.EmptySelection => "orders.fromSelection.problem.empty",
        OrderTargetSelectionPlanProblem.NoEligibleFeature => "orders.fromSelection.problem.noEligible",
        _ => "orders.fromSelection.problem.tooManyIds",
    };

    /// <summary>
    /// The message key for a failed preview or apply, by the exception the selection service (or
    /// its REST client) throws for each problem code. Anything unrecognised gets the generic one.
    /// </summary>
    public static string SelectionErrorKey(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            SelectionLimitExceededException => "orders.fromSelection.error.limitChanged",
            SelectionTooLargeException => "orders.fromSelection.error.tooLarge",
            InvalidSelectionFenceException => "orders.fromSelection.error.invalidFence",
            OrderActionDeniedException => "orders.fromSelection.error.denied",
            InvalidServiceOrderTransitionException => "orders.fromSelection.error.orderClosed",
            ServiceOrderConcurrencyException => "orders.fromSelection.error.concurrent",
            KeyNotFoundException => "orders.fromSelection.error.notFound",
            InvalidOperationException => "orders.fromSelection.error.noPolicy",
            ArgumentException => "orders.fromSelection.error.invalid",
            _ => "orders.fromSelection.error.failed",
        };
    }

    /// <summary>Every key the members above can return.</summary>
    public static IReadOnlyList<string> MessageKeys { get; } =
    [
        .. OrderTargetStatus.All.Select(StatusKey), UnknownStatusKey,
        .. Enum.GetValues<OrderTargetConflictLevel>().Select(ConflictKey),
        .. Enum.GetValues<OrderTargetSelectionPlanProblem>().Select(PlanProblemKey),
        "orders.fromSelection.error.limitChanged", "orders.fromSelection.error.tooLarge",
        "orders.fromSelection.error.invalidFence", "orders.fromSelection.error.denied",
        "orders.fromSelection.error.orderClosed", "orders.fromSelection.error.concurrent",
        "orders.fromSelection.error.notFound", "orders.fromSelection.error.noPolicy",
        "orders.fromSelection.error.invalid", "orders.fromSelection.error.failed",
    ];
}
