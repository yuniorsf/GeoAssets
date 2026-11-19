using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The neutral-operator clandestine-attachment-notice order type (XD01-263) — one order per
/// flagged pole attachment, built on <see cref="ClandestineAttachmentWorkflowGraph"/>.
/// Who may act on it is data the rule chain evaluates (XD01-364): the operator's staff create and flag notices and resolve
/// disputes; only an <see cref="IspRoles.Tenant"/> may contest one — also as a recipient of the notice's dispatch, the one
/// action a recipient may take beyond reading and commenting.
/// </summary>
public static class ClandestineAttachmentOrderTypes
{
    public const string ClandestineNoticeId = "isp-clandestine-notice";

    public static OrderType CreateClandestineNotice() => new()
    {
        Id = ClandestineNoticeId,
        DisplayName = "Clandestine Attachment Notice",
        Description = "Tracks a pole attachment flagged as undeclared (cabo à revelia) through its statutory 30-day regularization window.",
        InitialStateKey = ClandestineAttachmentWorkflowGraph.Active,
        States = ClandestineAttachmentWorkflowGraph.States(),
        Transitions = ClandestineAttachmentWorkflowGraph.Transitions(),
        CreationPolicies =
        [
            new(PolicyKind.Role, IspRoles.Supervisor),
            new(PolicyKind.Role, IspRoles.Administrator),
        ],
        ActionPermissions =
        [
            new(NeutralOperatorActions.Dispute, PolicyKind.Role, IspRoles.Tenant),
            new(NeutralOperatorActions.FlagClandestine, PolicyKind.Role, IspRoles.Supervisor),
            new(NeutralOperatorActions.FlagClandestine, PolicyKind.Role, IspRoles.Administrator),
            new(OrderAction.Approve, PolicyKind.Role, IspRoles.Supervisor),
            new(OrderAction.Approve, PolicyKind.Role, IspRoles.Administrator),
            new(OrderAction.Reject, PolicyKind.Role, IspRoles.Supervisor),
            new(OrderAction.Reject, PolicyKind.Role, IspRoles.Administrator),
        ],
        RecipientAccess = OrderRecipientAccess.Inform,
        RecipientActions = [NeutralOperatorActions.Dispute],
    };

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateClandestineNotice());
    }
}
