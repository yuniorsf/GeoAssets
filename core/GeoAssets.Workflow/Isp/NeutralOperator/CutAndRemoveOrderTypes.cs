using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The neutral-operator batched cleanup-drive cut order type (XD01-264) — one order per
/// street-corridor batch of expired, unregularized <c>CABO_A_REVELIA</c> pole attachments,
/// built on <see cref="CutAndRemoveWorkflowGraph"/>.
/// </summary>
public static class CutAndRemoveOrderTypes
{
    public const string CutAndRemoveId = "isp-cut-and-remove-os";

    public static OrderType CreateCutAndRemove() => new()
    {
        Id = CutAndRemoveId,
        DisplayName = "Cut and Remove Order",
        Description = "Batches expired, unregularized clandestine pole attachments along a street corridor into one multi-asset cut-and-remove order, dispatched to transit/utility/crew.",
        InitialStateKey = CutAndRemoveWorkflowGraph.Open,
        States = CutAndRemoveWorkflowGraph.States(),
        Transitions = CutAndRemoveWorkflowGraph.Transitions(),
    };

    public static void RegisterAll(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register(CreateCutAndRemove());
    }
}
