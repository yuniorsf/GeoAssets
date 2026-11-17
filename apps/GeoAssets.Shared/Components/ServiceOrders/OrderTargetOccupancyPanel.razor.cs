using GeoAssets.Shared.Services.OrderTargets;
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
///
/// XD01-313: a caller who may edit also gets a "Work" button per target, which opens
/// <see cref="OrderTargetWorkEditor"/> (activities, status, note) beneath the grid; and while the
/// panel shows an order, its targets carry their work state on the map
/// (<see cref="OrderTargetMapStates"/>) — for every viewer, read-only ones included.
/// </summary>
public partial class OrderTargetOccupancyPanel
{
    private readonly CancellationTokenSource _lifetime = new();

    private string? _loadedOrderId;
    private ServiceOrderTarget? _selected;
    private ServiceOrderTarget? _working;
    private string? _savedLabel;
    private OrderTargetsTable? _table;

    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>
    /// Whether the editors offer editing controls; the host still decides what is allowed. The
    /// per-target work editor is not offered at all without it. A recipient of the order's dispatch
    /// whose order type lets it execute the order gets it without the occupancy (XD01-333).
    /// </summary>
    [Parameter] public bool CanEdit { get; set; }

    /// <summary>
    /// Whether each target offers its pole's occupancy. False for a caller the occupancy is not
    /// for — the organization an order was dispatched to (XD01-316) reads the order's targets and
    /// the work asked on each, never the pole owner's occupancy — and the list is then read-only
    /// unless <see cref="CanEdit"/> offers it the per-target work.
    /// </summary>
    [Parameter] public bool CanOpenOccupancy { get; set; } = true;

    /// <summary>The order type's activity catalog, which names the activities of each target.</summary>
    private IReadOnlyList<OrderTargetActivity>? ActivityCatalog => OrderTypes.Find(Order.OrderTypeId)?.TargetPolicy?.Activities;

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Order.Id, _loadedOrderId, StringComparison.Ordinal)) return;

        _loadedOrderId = Order.Id;
        (_selected, _working, _savedLabel) = (null, null, null);
        await MapStates.ShowAsync(this, Order.Id, _lifetime.Token);
    }

    private void Select(ServiceOrderTarget target)
    {
        _selected = string.Equals(_selected?.Id, target.Id, StringComparison.Ordinal) ? null : target;
        (_working, _savedLabel) = (null, null);
    }

    private void Work(ServiceOrderTarget target)
    {
        _working = string.Equals(_working?.Id, target.Id, StringComparison.Ordinal) ? null : target;
        (_selected, _savedLabel) = (null, null);
    }

    private void CloseEditor() => _selected = null;

    private void CloseWork() => _working = null;

    /// <summary>
    /// The stored target changed: the grid and the map show it. After this editor's own save the
    /// editor closes with a confirmation; after a conflict it stays open on the newer values.
    /// </summary>
    private async Task OnWorkStoredAsync(OrderTargetWorkResult result)
    {
        if (result.Saved) (_working, _savedLabel) = (null, result.Target.Alias ?? result.Target.FeatureId);
        else _working = result.Target;

        await MapStates.RefreshAsync(result.Target);
        if (_table is not null) await _table.ReloadAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
            _ = MapStates.ClearAsync(this);
        }

        base.Dispose(disposing);
    }
}
