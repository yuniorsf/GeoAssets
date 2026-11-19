using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.OrderDetail;

/// <summary>
/// <see cref="IOrderDetailPanelRegistry"/> over the registrations made with
/// <see cref="OrderDetailPanelServiceExtensions.AddOrderDetailPanel{TComponent}"/> and
/// <see cref="OrderDetailPanelServiceExtensions.AddOrderDispatchDialog{TComponent}"/>, kept in
/// registration order.
/// </summary>
public sealed class OrderDetailPanelRegistry(
    IEnumerable<OrderDetailPanelRegistration> panels,
    IEnumerable<OrderDispatchDialogRegistration> dispatchDialogs) : IOrderDetailPanelRegistry
{
    private readonly OrderDetailPanelRegistration[] _panels = [.. panels];
    private readonly OrderDispatchDialogRegistration[] _dispatchDialogs = [.. dispatchDialogs];

    /// <inheritdoc />
    public IReadOnlyList<Type> PanelsFor(OrderType? orderType) =>
        orderType is null ? [] : [.. _panels.Where(p => orderType.HasCapability(p.Capability)).Select(p => p.Component)];

    /// <inheritdoc />
    public Type? DispatchDialogFor(OrderType? orderType) =>
        orderType is null ? null : _dispatchDialogs.FirstOrDefault(d => d.AppliesTo(orderType))?.Component;
}
