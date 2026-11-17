using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// An order's targets with the way into <see cref="OccupancyChangeEditor"/> (XD01-315): the paged
/// targets grid (<see cref="OrderTargetsTable"/>, XD01-312) with the work asked on each target
/// (XD01-316) and one "Occupancy" button per target, which opens the editor for that pole beneath
/// the grid. For a caller the occupancy is not for, the grid is read-only (XD01-316).
/// </summary>
public partial class OrderTargetOccupancyPanel
{
    private string? _loadedOrderId;
    private ServiceOrderTarget? _selected;

    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>Whether the editor offers editing controls; the host still decides what is allowed.</summary>
    [Parameter] public bool CanEdit { get; set; }

    /// <summary>
    /// Whether each target offers its pole's occupancy. False for a caller the occupancy is not
    /// for — the organization an order was dispatched to (XD01-316) reads the order's targets and
    /// the work asked on each, never the pole owner's occupancy — and the list is then read-only.
    /// </summary>
    [Parameter] public bool CanOpenOccupancy { get; set; } = true;

    /// <summary>The order type's activity catalog, which names the activities of each target.</summary>
    private IReadOnlyList<OrderTargetActivity>? ActivityCatalog => OrderTypes.Find(Order.OrderTypeId)?.TargetPolicy?.Activities;

    protected override void OnParametersSet()
    {
        if (string.Equals(Order.Id, _loadedOrderId, StringComparison.Ordinal)) return;

        _loadedOrderId = Order.Id;
        _selected = null;
    }

    private void Select(ServiceOrderTarget target) =>
        _selected = string.Equals(_selected?.Id, target.Id, StringComparison.Ordinal) ? null : target;

    private void CloseEditor() => _selected = null;
}
