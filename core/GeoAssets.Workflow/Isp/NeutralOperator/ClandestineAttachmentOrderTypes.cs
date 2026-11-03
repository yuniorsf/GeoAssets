using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The neutral-operator clandestine-attachment-notice order type (XD01-263) — one order per
/// flagged pole attachment, built on <see cref="ClandestineAttachmentWorkflowGraph"/>.
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
    };

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateClandestineNotice());
    }
}
