using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.OrderDetail;

/// <summary>
/// The components a vertical contributes to the generic order detail (XD01-378): the panels an
/// order of a type shows, and the dialog that dispatches it instead of the generic one. The detail
/// renders them through <c>DynamicComponent</c>, so the components it never references by type
/// receive their parameters by name:
/// <list type="bullet">
/// <item>a <b>panel</b> receives <c>Order</c> (<see cref="IServiceOrder"/>), <c>Principal</c>
/// (<see cref="Workflow.Rules.WorkflowPrincipal"/>) and <c>OnChanged</c>
/// (<see cref="Microsoft.AspNetCore.Components.EventCallback"/>, raised after the panel changed
/// the order so the host reloads it);</item>
/// <item>a <b>dispatch dialog</b> receives <c>Visible</c> (<see cref="bool"/>), <c>Order</c>,
/// <c>Principal</c>, <c>OnDispatched</c> (<see cref="Microsoft.AspNetCore.Components.EventCallback"/>,
/// raised after a dispatch went through so the host reloads the order) and <c>OnCancel</c>
/// (<see cref="Microsoft.AspNetCore.Components.EventCallback"/>).</item>
/// </list>
/// A component must declare every one of those parameters.
/// </summary>
public interface IOrderDetailPanelRegistry
{
    /// <summary>
    /// The panels an order of <paramref name="orderType"/> shows: every registered panel whose
    /// capability the type declares, in registration order. Empty for an unknown type.
    /// </summary>
    IReadOnlyList<Type> PanelsFor(OrderType? orderType);

    /// <summary>
    /// The dialog that dispatches an order of <paramref name="orderType"/>: the first registered
    /// dialog that applies to the type; null when none does (or the type is unknown), and the
    /// generic dispatch dialog is used.
    /// </summary>
    Type? DispatchDialogFor(OrderType? orderType);
}
