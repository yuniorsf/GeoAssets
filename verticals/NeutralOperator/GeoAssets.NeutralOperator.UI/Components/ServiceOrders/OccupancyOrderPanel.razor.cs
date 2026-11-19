using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.UI.Services.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.NeutralOperator.UI.Components.ServiceOrders;

/// <summary>
/// The order-detail panel of an order whose type commits occupancy (XD01-378, registered for
/// <see cref="OccupancyCapabilities.Commit"/>): the order's targets with their poles' occupancy
/// (<see cref="OrderTargetOccupancyPanel"/>, XD01-315) and the approval that commits the occupancy
/// staged on them (XD01-318). Receives the panel parameters of
/// <c>GeoAssets.Shared.Services.OrderDetail.IOrderDetailPanelRegistry</c>.
/// </summary>
public partial class OccupancyOrderPanel
{
    private bool _approving;
    private string? _approvalErrorKey;

    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>Raised after the panel approved the order, so the host reloads it.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    private OrderType? OrderType => OrderTypes.Find(Order.OrderTypeId);

    /// <summary>Only orders whose type commits occupancy and declares a target policy have poles to edit the occupancy of (XD01-315, XD01-361).</summary>
    private bool Commits => OccupancyApproval.Commits(OrderType);

    /// <summary>Approving the order commits the occupancy staged on its poles; only the pole owner may (XD01-318).</summary>
    private bool CanApprove => OrderApprovalState.CanApprove(OrderType, Order, Principal, Rules);

    /// <summary>
    /// Whether the viewer may record the work on the order's targets: what it may annotate, and — for
    /// a recipient of the order's dispatch — only when the order's type lets a recipient execute it (XD01-333).
    /// </summary>
    private bool CanEdit => Rules.EvaluateTargetWrite(Principal, Order, OrderTargetWrite.RecordWork).Allowed;

    /// <summary>The viewer reaches the order only because it was dispatched to it: its poles' occupancy is not shown (XD01-316).</summary>
    private bool CanOpenOccupancy => !Rules.IsDispatchRecipientOnly(Principal, Order);

    private async Task Approve()
    {
        _approving = true;
        _approvalErrorKey = null;
        try
        {
            await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
                Action: OrderAction.Approve,
                PerformedBy: Principal.UserId,
                PerformedAt: Clock.GetUtcNow().UtcDateTime,
                ResultingStatus: OccupancyApproval.ApprovedStatus(OrderType, Order.Status)!));

            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            _approvalErrorKey = OrderApprovalState.ErrorKey(ex);
        }
        finally
        {
            _approving = false;
        }
    }
}
