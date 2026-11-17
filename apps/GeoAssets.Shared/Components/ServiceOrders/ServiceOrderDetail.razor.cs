using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

public partial class ServiceOrderDetail
{
    [Parameter] public IServiceOrder? Order { get; set; }
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnChanged { get; set; }

    private bool _dispatchDialogVisible;
    private string _noteText = string.Empty;
    private bool _approving;
    private string? _approvalErrorKey;

    private bool CanDispatch    => Order is not null && Rules.Evaluate(Principal, OrderActionType.Dispatch, Order).Allowed;
    private bool CanAssignToMe  => Order is not null && !string.Equals(Order.AssignedTo, Principal.UserId, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderActionType.Assign, Order).Allowed;
    private bool CanStart       => Order is not null && string.Equals(Order.Status, ServiceOrderStatus.Pending, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderActionType.Execute, Order).Allowed;
    private bool CanComplete    => Order is not null && string.Equals(Order.Status, ServiceOrderStatus.InProgress, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderActionType.Complete, Order).Allowed;
    private bool CanCancel      => Order is not null
                                    && Order.Status is ServiceOrderStatus.Draft or ServiceOrderStatus.Pending
                                                    or ServiceOrderStatus.InProgress or ServiceOrderStatus.OnHold
                                    && Rules.Evaluate(Principal, OrderActionType.Cancel, Order).Allowed;
    private bool CanAnnotate    => Order is not null && Rules.Evaluate(Principal, OrderActionType.Annotate, Order).Allowed;

    /// <summary>Approving an order with targets commits the occupancy staged on its poles; only the pole owner may (XD01-318).</summary>
    private bool CanApprove     => Order is not null && OrderApprovalState.CanApprove(OrderTypes.Find(Order.OrderTypeId), Order, Principal, Rules);

    /// <summary>Only orders whose type declares a target policy have targets, and so poles to edit the occupancy of (XD01-315).</summary>
    private bool HasTargets     => Order is not null && OrderTypes.Find(Order.OrderTypeId)?.TargetPolicy is not null;

    /// <summary>An order whose type fans out to its poles' occupants is dispatched through the shared-use dialog (XD01-316).</summary>
    private bool IsFanOut       => Order is not null && SharedUseDispatchDialogState.IsFanOut(OrderTypes.Find(Order.OrderTypeId));

    /// <summary>The viewer reaches the order only because it was dispatched to it: its targets are read-only and its poles' occupancy is not shown (XD01-316).</summary>
    private bool IsRecipientOnly => Order is not null && Rules.IsDispatchRecipientOnly(Principal, Order);

    private void OpenDispatchDialog() => _dispatchDialogVisible = true;

    /// <summary>The dialog stays open on its result; the order behind it is reloaded with its new status and children.</summary>
    private Task OnSharedUseDispatched(SharedUseDispatchResult result) => OnChanged.InvokeAsync();

    private async Task ConfirmDispatch((string TargetId, DispatchTargetType TargetType, string? Note) result)
    {
        if (Order is null) return;

        await Repository.AppendDispatchAsync(Order.Id,
            new OrderDispatch(result.TargetId, result.TargetType, Principal.UserId, Clock.GetUtcNow().UtcDateTime, result.Note));

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: OrderActionType.Dispatch,
            PerformedBy: Principal.UserId,
            PerformedAt: Clock.GetUtcNow().UtcDateTime,
            Comment: result.Note,
            ResultingStatus: string.Equals(Order.Status, ServiceOrderStatus.Draft, StringComparison.Ordinal) ? ServiceOrderStatus.Pending : null));

        _dispatchDialogVisible = false;
        await OnChanged.InvokeAsync();
    }

    private async Task AssignToMe()
    {
        if (Order is null) return;
        await Repository.UpdateAsync(BuildSnapshot(assignedTo: Principal.UserId));
        await OnChanged.InvokeAsync();
    }

    private async Task Start()
    {
        if (Order is null) return;
        await RecordAction(OrderActionType.Execute, ServiceOrderStatus.InProgress);
    }

    private async Task Complete()
    {
        if (Order is null) return;
        await RecordAction(OrderActionType.Complete, ServiceOrderStatus.Completed);
    }

    private async Task Approve()
    {
        if (Order is null) return;

        _approving = true;
        _approvalErrorKey = null;
        try
        {
            await RecordAction(OrderActionType.Approve, OccupancyApproval.ApprovedStatus(OrderTypes.Find(Order.OrderTypeId), Order.Status)!);
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

    private async Task CancelOrder()
    {
        if (Order is null) return;
        await RecordAction(OrderActionType.Cancel, ServiceOrderStatus.Cancelled);
    }

    private async Task AddAnnotation()
    {
        if (Order is null || string.IsNullOrWhiteSpace(_noteText)) return;

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: OrderActionType.Annotate,
            PerformedBy: Principal.UserId,
            PerformedAt: Clock.GetUtcNow().UtcDateTime,
            Comment: _noteText));

        _noteText = string.Empty;
        await OnChanged.InvokeAsync();
    }

    private async Task RecordAction(OrderActionType action, string resultingStatus)
    {
        if (Order is null) return;

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: action,
            PerformedBy: Principal.UserId,
            PerformedAt: Clock.GetUtcNow().UtcDateTime,
            ResultingStatus: resultingStatus));

        await OnChanged.InvokeAsync();
    }

    /// <summary>
    /// Builds a scalar-field snapshot for <see cref="IServiceOrderWriter.UpdateAsync"/>, which
    /// only persists scalar fields (not Dispatches/ActionLog — see its doc comment).
    /// </summary>
    private ServiceOrder BuildSnapshot(string? assignedTo = null)
    {
        var order = Order!;
        return new ServiceOrder
        {
            Id            = order.Id,
            RowVersion    = order.RowVersion,
            Title         = order.Title,
            Description   = order.Description,
            OrderTypeId   = order.OrderTypeId,
            Status        = order.Status,
            Priority      = order.Priority,
            // XD01-332: the organization never changes; the snapshot states the stored one.
            OrganizationId = order.OrganizationId,
            AssignedTo    = assignedTo ?? order.AssignedTo,
            UpdatedAt     = Clock.GetUtcNow().UtcDateTime,
            ScheduledAt   = order.ScheduledAt,
            CompletedAt   = order.CompletedAt,
            ParentOrderId = order.ParentOrderId,
            SelectionSpec = order.SelectionSpec,
            FeatureIds    = [.. order.Features.Select(f => f.Id)],
            Attributes    = new Dictionary<string, string>(order.Attributes, StringComparer.Ordinal),
            Features      = [.. order.Features],
        };
    }

    private Task Close() => OnClose.InvokeAsync();
}
