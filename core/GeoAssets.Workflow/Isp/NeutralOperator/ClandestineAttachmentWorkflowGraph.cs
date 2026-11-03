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
///
/// <see cref="Disputed"/> was added for XD01-269's tenant self-service portal: a flagged
/// tenant can contest the flag by submitting evidence, moving the order into a reviewable
/// dispute state rather than either staying silently flagged or being treated as regularized.
/// An operator then resolves the dispute by either accepting it (→ <see cref="Regularized"/>,
/// same terminal state a real fix reaches) or rejecting it (back to <see cref="CaboARevelia"/>,
/// where the original statutory deadline — stamped once at flag-time and never reset — still
/// applies).
/// </summary>
public static class ClandestineAttachmentWorkflowGraph
{
    public const string Active = "Active";
    public const string CaboARevelia = "CaboARevelia";
    public const string Disputed = "Disputed";
    public const string Regularized = "Regularized";

    /// <summary>Fresh, independently-mutable copy each call — see <c>IpBillingWorkflowGraph.States</c>.</summary>
    public static List<WorkflowState> States() =>
    [
        new WorkflowState(Active,       "Active"),
        new WorkflowState(CaboARevelia, "Cabo à Revelia"),
        new WorkflowState(Disputed,     "Disputed"),
        new WorkflowState(Regularized,  "Regularized", IsSuccess: true),
    ];

    /// <summary>Fresh, independently-mutable copy each call.</summary>
    public static List<WorkflowTransition> Transitions() =>
    [
        new WorkflowTransition(Active,       CaboARevelia, OrderActionType.FlagClandestine),
        new WorkflowTransition(CaboARevelia, Regularized,  OrderActionType.Approve),
        new WorkflowTransition(CaboARevelia, Disputed,     OrderActionType.Dispute),
        new WorkflowTransition(Disputed,     Regularized,  OrderActionType.Approve),
        new WorkflowTransition(Disputed,     CaboARevelia, OrderActionType.Reject),
    ];
}
