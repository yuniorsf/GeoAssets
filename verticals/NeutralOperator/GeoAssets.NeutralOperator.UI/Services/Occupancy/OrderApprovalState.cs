using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.UI.Services.Occupancy;

/// <summary>
/// When <c>ServiceOrderDetail</c> offers to approve an order whose approval commits staged
/// occupancy (XD01-318), and which localization key it shows when the approval fails. Kept apart
/// from the component so both are testable without rendering it.
/// </summary>
public static class OrderApprovalState
{
    /// <summary>
    /// True when <paramref name="principal"/> may approve <paramref name="order"/> here: its type
    /// commits staged occupancy on approval, its status has an approval transition, the caller is
    /// of the order's organization — only the pole owner commits its occupancy — and the rules
    /// allow <see cref="OrderAction.Approve"/>.
    /// </summary>
    public static bool CanApprove(OrderType? orderType, IServiceOrder order, WorkflowPrincipal principal, ServiceOrderRules rules)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(rules);

        return OccupancyApproval.Commits(orderType)
            && OccupancyApproval.ApprovedStatus(orderType, order.Status) is not null
            && Guid.TryParse(principal.OrganizationId, out var organizationId)
            && organizationId == order.OrganizationId
            && rules.Evaluate(principal, OrderAction.Approve, order).Allowed;
    }

    /// <summary>The key for a failed approval, most specific first.</summary>
    public static string ErrorKey(Exception exception) => exception switch
    {
        OccupancyConflictException => "orderApproval.error.conflict",
        ServiceOrderConcurrencyException => "orderApproval.error.concurrency",
        InvalidServiceOrderTransitionException => "orderApproval.error.invalidState",
        OrderActionDeniedException => "orderApproval.error.denied",
        KeyNotFoundException => "orderApproval.error.notFound",
        _ => "orderApproval.error.unexpected",
    };

    /// <summary>Every key the detail page shows for an approval: the button, its busy label and what <see cref="ErrorKey"/> returns.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "orderApproval.approve", "orderApproval.approving",
        "orderApproval.error.conflict", "orderApproval.error.concurrency", "orderApproval.error.invalidState",
        "orderApproval.error.denied", "orderApproval.error.notFound", "orderApproval.error.unexpected",
    ];
}
