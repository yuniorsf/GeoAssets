using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The <c>Active→CaboARevelia→Regularized</c> workflow graph for a neutral-operator pole
/// attachment's clandestine-fiber lifecycle (XD01-263). Deliberately its own dedicated graph —
/// not a state bolted onto the shared <c>FieldWorkWorkflowGraph</c> (which other pole-related
/// order types, e.g. <see cref="PoleCensusOrderTypes"/>, reuse as-is for field-dispatch shaped
/// work) — because tracking a flagged attachment's statutory countdown and regularization is a
/// distinct lifecycle, not a field-work dispatch. Mirrors
/// <c>GeoAssets.IpBilling.Workflow.IpBillingWorkflowGraph</c>'s shape: states/transitions as
/// plain data, independently-mutable per call.
/// </summary>
public static class ClandestineAttachmentWorkflowGraph
{
    public const string Active = "Active";
    public const string CaboARevelia = "CaboARevelia";
    public const string Regularized = "Regularized";

    /// <summary>Fresh, independently-mutable copy each call — see <c>IpBillingWorkflowGraph.States</c>.</summary>
    public static List<WorkflowState> States() =>
    [
        new WorkflowState(Active,       "Active"),
        new WorkflowState(CaboARevelia, "Cabo à Revelia"),
        new WorkflowState(Regularized,  "Regularized", IsSuccess: true),
    ];

    /// <summary>Fresh, independently-mutable copy each call.</summary>
    public static List<WorkflowTransition> Transitions() =>
    [
        new WorkflowTransition(Active,       CaboARevelia, OrderActionType.FlagClandestine),
        new WorkflowTransition(CaboARevelia, Regularized,  OrderActionType.Approve),
    ];
}
