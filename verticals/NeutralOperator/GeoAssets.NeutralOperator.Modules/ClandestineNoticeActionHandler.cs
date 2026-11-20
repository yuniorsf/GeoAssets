using GeoAssets.NeutralOperator.Application;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// The clandestine-notice actions (XD01-364, ADR-001 smell H) as the NeutralOperator module's
/// <see cref="IOrderActionHandler"/>: reached only after the rule chain allowed the action — through
/// <c>POST /service-orders/{id}/actions</c>, or the tenant portal's dispute route, which evaluates the same requirement —
/// and performed by <see cref="IClandestineAttachmentService"/>, which stamps the notice's deadlines.
/// <list type="bullet">
///   <item><see cref="NeutralOperatorActions.FlagClandestine"/>: Active → CaboARevelia (flag).</item>
///   <item><see cref="NeutralOperatorActions.Dispute"/>: CaboARevelia → Disputed; the entry's comment is the evidence reference.</item>
///   <item><see cref="OrderAction.Approve"/>: CaboARevelia → Regularized (regularize, within the window), or
///   Disputed → Regularized (dispute upheld).</item>
///   <item><see cref="OrderAction.Reject"/>: Disputed → CaboARevelia (dispute rejected).</item>
/// </list>
/// The entry's performer and instant are not used: the action is logged as the caller, at the service's clock.
/// </summary>
public sealed class ClandestineNoticeActionHandler(IClandestineAttachmentService notices) : IOrderActionHandler
{
    private static readonly OrderAction[] HandledActions =
        [NeutralOperatorActions.FlagClandestine, NeutralOperatorActions.Dispute, OrderAction.Approve, OrderAction.Reject];

    public bool CanHandle(OrderType? orderType, OrderActionLog entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.Equals(orderType?.Id, ClandestineAttachmentOrderTypes.ClandestineNoticeId, StringComparison.Ordinal)
            && HandledActions.Contains(entry.Action);
    }

    public async Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (order, _, entry, principal) = context;
        var target = TargetStatus(entry.Action, order.Status);
        if (target is null || (entry.ResultingStatus is { } requested && !string.Equals(requested, target, StringComparison.Ordinal)))
            return OrderActionOutcome.InvalidTransition(order.Status, entry.ResultingStatus ?? target);

        if (entry.Action == NeutralOperatorActions.Dispute && string.IsNullOrWhiteSpace(entry.Comment))
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
    internal static string? TargetStatus(OrderAction action, string status)
    {
        if (action == NeutralOperatorActions.FlagClandestine)
            return Is(status, ClandestineAttachmentWorkflowGraph.Active) ? ClandestineAttachmentWorkflowGraph.CaboARevelia : null;
        if (action == NeutralOperatorActions.Dispute)
            return Is(status, ClandestineAttachmentWorkflowGraph.CaboARevelia) ? ClandestineAttachmentWorkflowGraph.Disputed : null;
        if (action == OrderAction.Approve)
        {
            return Is(status, ClandestineAttachmentWorkflowGraph.CaboARevelia) || Is(status, ClandestineAttachmentWorkflowGraph.Disputed)
                ? ClandestineAttachmentWorkflowGraph.Regularized
                : null;
        }

        return action == OrderAction.Reject && Is(status, ClandestineAttachmentWorkflowGraph.Disputed)
            ? ClandestineAttachmentWorkflowGraph.CaboARevelia
            : null;
    }

    private static bool Is(string status, string state) => string.Equals(status, state, StringComparison.Ordinal);

    private Task<ClandestineFlagResult> PerformAsync(IServiceOrder order, OrderActionLog entry, string performedBy, CancellationToken ct)
    {
        if (entry.Action == NeutralOperatorActions.FlagClandestine) return notices.FlagAsync(order.Id, performedBy, ct);
        if (entry.Action == NeutralOperatorActions.Dispute) return notices.DisputeAsync(order.Id, entry.Comment!, performedBy, ct);
        if (entry.Action == OrderAction.Approve && Is(order.Status, ClandestineAttachmentWorkflowGraph.CaboARevelia))
            return notices.RegularizeAsync(order.Id, performedBy, ct);

        return notices.ResolveDisputeAsync(order.Id, upheld: entry.Action == OrderAction.Approve, performedBy, ct);
    }

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
