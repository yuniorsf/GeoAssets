using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The <c>Submitted→Approved</c>/<c>Rejected</c> workflow graph for a tenant ISP's new
/// pole-attachment request (XD01-269) — mirrors <see cref="ClandestineAttachmentWorkflowGraph"/>'s
/// shape: states/transitions as plain data, independently-mutable per call. Deliberately
/// terminal on rejection rather than looping back — a rejected request is resubmitted as a new
/// order, not reopened, matching how a denied request is handled elsewhere in this codebase
/// (e.g. the generic <c>ServiceOrderStatus</c> graph's own terminal failure states).
/// </summary>
public static class TenantAttachmentRequestWorkflowGraph
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    /// <summary>Fresh, independently-mutable copy each call.</summary>
    public static List<WorkflowState> States() =>
    [
        new WorkflowState(Submitted, "Submitted"),
        new WorkflowState(Approved,  "Approved", IsSuccess: true),
        new WorkflowState(Rejected,  "Rejected"),
    ];

    /// <summary>Fresh, independently-mutable copy each call.</summary>
    public static List<WorkflowTransition> Transitions() =>
    [
        new WorkflowTransition(Submitted, Approved, OrderActionType.Approve),
        new WorkflowTransition(Submitted, Rejected, OrderActionType.Reject),
    ];
}
