using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The <c>Open→Dispatched→Completed</c> workflow graph for a batched, multi-pole
/// <c>CUT_AND_REMOVE_OS</c> cleanup-drive order (XD01-264). A dedicated graph, mirroring
/// <see cref="ClandestineAttachmentWorkflowGraph"/>'s shape — states/transitions as plain data,
/// independently-mutable per call.
/// </summary>
public static class CutAndRemoveWorkflowGraph
{
    public const string Open = "Open";
    public const string Dispatched = "Dispatched";
    public const string Completed = "Completed";

    /// <summary>Fresh, independently-mutable copy each call — see <c>IpBillingWorkflowGraph.States</c>.</summary>
    public static List<WorkflowState> States() =>
    [
        new WorkflowState(Open,       "Open"),
        new WorkflowState(Dispatched, "Dispatched"),
        new WorkflowState(Completed,  "Completed", IsSuccess: true),
    ];

    /// <summary>Fresh, independently-mutable copy each call.</summary>
    public static List<WorkflowTransition> Transitions() =>
    [
        new WorkflowTransition(Open,       Dispatched, OrderActionType.Dispatch),
        new WorkflowTransition(Dispatched, Completed,  OrderActionType.Complete),
    ];
}
