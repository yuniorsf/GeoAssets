using GeoAssets.Workflow.Orders;

namespace GeoAssets.IpBilling.Workflow;

/// <summary>
/// The <c>Carregado→Calculado→Validado(+Cancelado)</c> workflow graph shared by all three
/// IP-billing <see cref="OrderType"/> definitions (Levantamento/Fiscalização/Ofício —
/// XD01-179/182/185 each build their own <see cref="OrderType"/> but reuse these exact
/// <see cref="States"/>/<see cref="Transitions"/> rather than redefining them). Deliberately
/// not the generic Draft/Pending/.../Completed graph (<c>ServiceOrderTransitions</c>'s
/// default) — spreadsheet-upload workflows have no dispatch/assignment stage.
/// </summary>
public static class IpBillingWorkflowGraph
{
    public const string Carregado = "Carregado";
    public const string Calculado = "Calculado";
    public const string Validado  = "Validado";
    public const string Cancelado = "Cancelado";

    /// <summary>
    /// Fresh, independently-mutable copy each call — <see cref="OrderType.States"/> is a plain
    /// <see cref="List{T}"/> a caller may legally append to, so handing out a shared instance
    /// here would let one order type's customization leak into every other.
    /// </summary>
    public static List<WorkflowState> States() =>
    [
        new WorkflowState(Carregado, "Carregado"),
        new WorkflowState(Calculado, "Calculado"),
        new WorkflowState(Validado,  "Validado", IsSuccess: true),
        new WorkflowState(Cancelado, "Cancelado"),
    ];

    /// <summary>Fresh, independently-mutable copy each call — see <see cref="States"/>.</summary>
    public static List<WorkflowTransition> Transitions() =>
    [
        new WorkflowTransition(Carregado, Calculado, OrderActionType.Calculate),
        new WorkflowTransition(Calculado, Validado,  OrderActionType.Validate),
        new WorkflowTransition(Carregado, Cancelado, OrderActionType.Cancel),
        new WorkflowTransition(Calculado, Cancelado, OrderActionType.Cancel),
    ];
}
