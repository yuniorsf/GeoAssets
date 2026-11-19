using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The clandestine-notice actions (XD01-364, ADR-001 smell H) as the NeutralOperator module's
/// <see cref="IOrderActionHandler"/>: reached only after the rule chain allowed the action — through
/// <c>POST /service-orders/{id}/actions</c>, or the tenant portal's dispute route, which evaluates the same requirement —
/// and performed by <see cref="IClandestineAttachmentService"/>, which stamps the notice's deadlines.
/// <list type="bullet">
///   <item><see cref="OrderActionType.FlagClandestine"/>: Active → CaboARevelia (flag).</item>
///   <item><see cref="OrderActionType.Dispute"/>: CaboARevelia → Disputed; the entry's comment is the evidence reference.</item>
///   <item><see cref="OrderActionType.Approve"/>: CaboARevelia → Regularized (regularize, within the window), or
///   Disputed → Regularized (dispute upheld).</item>
///   <item><see cref="OrderActionType.Reject"/>: Disputed → CaboARevelia (dispute rejected).</item>
/// </list>
/// The entry's performer and instant are not used: the action is logged as the caller, at the service's clock.
/// </summary>
public sealed class ClandestineNoticeActionHandler(IClandestineAttachmentService notices) : IOrderActionHandler
{
    public bool CanHandle(OrderType? orderType, OrderActionLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.Equals(orderType?.Id, ClandestineAttachmentOrderTypes.ClandestineNoticeId, StringComparison.Ordinal)
            && entry.Action is OrderActionType.FlagClandestine or OrderActionType.Dispute
                or OrderActionType.Approve or OrderActionType.Reject;
    }

    public async Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (order, _, entry, principal) = context;
        var target = TargetStatus(entry.Action, order.Status);
        if (target is null || (entry.ResultingStatus is { } requested && !string.Equals(requested, target, StringComparison.Ordinal)))
            return OrderActionOutcome.InvalidTransition(order.Status, entry.ResultingStatus ?? target);

        if (entry.Action == OrderActionType.Dispute && string.IsNullOrWhiteSpace(entry.Comment))
        {
            return OrderActionOutcome.Problem(
                StatusCodes.Status400BadRequest, ClandestineProblemCodes.EvidenceRequired,
                "A dispute needs an evidence reference.", "Put the evidence reference in the action's comment.");
        }

        try
        {
            await PerformAsync(order, entry, principal.UserId, cancellationToken).ConfigureAwait(false);
            return OrderActionOutcome.Applied;
        }
        catch (Exception ex) when (FromException(ex, order.Status, target) is { } outcome)
        {
            return outcome;
        }
    }

    /// <summary>Where <paramref name="action"/> takes a notice in <paramref name="status"/>; null when it takes it nowhere.</summary>
    internal static string? TargetStatus(OrderActionType action, string status) => (action, status) switch
    {
        (OrderActionType.FlagClandestine, ClandestineAttachmentWorkflowGraph.Active) => ClandestineAttachmentWorkflowGraph.CaboARevelia,
        (OrderActionType.Dispute, ClandestineAttachmentWorkflowGraph.CaboARevelia) => ClandestineAttachmentWorkflowGraph.Disputed,
        (OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.CaboARevelia) => ClandestineAttachmentWorkflowGraph.Regularized,
        (OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.Disputed) => ClandestineAttachmentWorkflowGraph.Regularized,
        (OrderActionType.Reject, ClandestineAttachmentWorkflowGraph.Disputed) => ClandestineAttachmentWorkflowGraph.CaboARevelia,
        _ => null,
    };

    private Task<ClandestineFlagResult> PerformAsync(IServiceOrder order, OrderActionLog entry, string performedBy, CancellationToken ct) =>
        (entry.Action, order.Status) switch
        {
            (OrderActionType.FlagClandestine, _) => notices.FlagAsync(order.Id, performedBy, ct),
            (OrderActionType.Dispute, _) => notices.DisputeAsync(order.Id, entry.Comment!, performedBy, ct),
            (OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.CaboARevelia) => notices.RegularizeAsync(order.Id, performedBy, ct),
            (OrderActionType.Approve, _) => notices.ResolveDisputeAsync(order.Id, upheld: true, performedBy, ct),
            _ => notices.ResolveDisputeAsync(order.Id, upheld: false, performedBy, ct),
        };

    /// <summary>The outcome for what the service threw; null for anything that is not a client-facing failure.</summary>
    internal static OrderActionOutcome? FromException(Exception exception, string from, string to) => exception switch
    {
        ClandestineNoticeWindowElapsedException elapsed => OrderActionOutcome.Problem(
            StatusCodes.Status409Conflict, ClandestineProblemCodes.WindowElapsed,
            "The notice's regularization window has elapsed.", elapsed.Message),
        KeyNotFoundException => OrderActionOutcome.NotFound,
        ServiceOrderConcurrencyException => OrderActionOutcome.ConcurrentModification,
        InvalidServiceOrderTransitionException transition => OrderActionOutcome.InvalidTransition(transition.From, transition.To),
        InvalidOperationException => OrderActionOutcome.InvalidTransition(from, to),
        _ => null,
    };
}
