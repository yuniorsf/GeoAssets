using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Turns a selection (a fence drawn on the map, a pasted identifier list, …) into targets of
/// one service order whose type declares an <see cref="OrderTargetPolicy"/>.
///
/// Both members share these rules, checked in this order:
/// a null argument throws <see cref="ArgumentNullException"/>; a blank order or strategy id
/// throws <see cref="ArgumentException"/>; an unknown order throws
/// <see cref="KeyNotFoundException"/>; a principal the order rules refuse throws
/// <see cref="OrderActionDeniedException"/> before any target is read; an unregistered order
/// type throws <see cref="KeyNotFoundException"/>; an order type without a target policy throws
/// <see cref="InvalidOperationException"/>; an unknown strategy throws
/// <see cref="KeyNotFoundException"/>. Parameter errors surface as the strategy threw them.
///
/// The selection is always restricted to the policy's asset types and to the order's own
/// organization, whatever the request says, and a selection matching more than
/// <see cref="OrderTargetSelectionOptions.MaxMatched"/> features throws
/// <see cref="SelectionTooLargeException"/>.
/// </summary>
public interface IOrderTargetSelectionService
{
    /// <summary>
    /// Reports what applying <paramref name="request"/> would do, and writes nothing. Requires
    /// <see cref="Orders.OrderAction.View"/> on the order. A selection that does not fit
    /// under the selection limit is reported through
    /// <see cref="TargetSelectionPreview.TruncatedByLimit"/>, not rejected.
    /// </summary>
    Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>
    /// Adds the selected features to the order as targets, records the selection on the order
    /// and appends one <see cref="Orders.OrderAction.Annotate"/> action-log entry carrying
    /// the counts. Requires <see cref="Orders.OrderAction.Annotate"/> on the order.
    /// <list type="bullet">
    /// <item><description>
    /// An order in a terminal state throws
    /// <see cref="Orders.InvalidServiceOrderTransitionException"/>.
    /// </description></item>
    /// <item><description>
    /// A selection that does not fit under the selection limit throws
    /// <see cref="SelectionLimitExceededException"/> and writes nothing, unless
    /// <see cref="TargetSelectionRequest.AcceptTruncation"/> is set, in which case the part that
    /// fits (lowest feature ids first) is added.
    /// </description></item>
    /// <item><description>
    /// Safe to repeat: features the order already holds are skipped, so an identical second
    /// call adds 0 targets.
    /// </description></item>
    /// </list>
    /// </summary>
    Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct);
}
