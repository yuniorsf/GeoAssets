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

    private bool CanDispatch    => Order is not null && Rules.Evaluate(Principal, OrderActionType.Dispatch, Order).Allowed;
    private bool CanAssignToMe  => Order is not null && Order.AssignedTo != Principal.UserId
                                    && Rules.Evaluate(Principal, OrderActionType.Assign, Order).Allowed;
    private bool CanStart       => Order is not null && Order.Status == ServiceOrderStatus.Pending
                                    && Rules.Evaluate(Principal, OrderActionType.Execute, Order).Allowed;
    private bool CanComplete    => Order is not null && Order.Status == ServiceOrderStatus.InProgress
                                    && Rules.Evaluate(Principal, OrderActionType.Complete, Order).Allowed;
    private bool CanCancel      => Order is not null
                                    && Order.Status is ServiceOrderStatus.Draft or ServiceOrderStatus.Pending
                                                    or ServiceOrderStatus.InProgress or ServiceOrderStatus.OnHold
                                    && Rules.Evaluate(Principal, OrderActionType.Cancel, Order).Allowed;
    private bool CanAnnotate    => Order is not null && Rules.Evaluate(Principal, OrderActionType.Annotate, Order).Allowed;

    private void OpenDispatchDialog() => _dispatchDialogVisible = true;

    private async Task ConfirmDispatch((string TargetId, DispatchTargetType TargetType, string? Note) result)
    {
        if (Order is null) return;

        await Repository.AppendDispatchAsync(Order.Id,
            new OrderDispatch(result.TargetId, result.TargetType, Principal.UserId, DateTime.UtcNow, result.Note));

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: OrderActionType.Dispatch,
            PerformedBy: Principal.UserId,
            PerformedAt: DateTime.UtcNow,
            Comment: result.Note,
            ResultingStatus: Order.Status == ServiceOrderStatus.Draft ? ServiceOrderStatus.Pending : null));

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
            PerformedAt: DateTime.UtcNow,
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
            PerformedAt: DateTime.UtcNow,
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
            Title         = order.Title,
            Description   = order.Description,
            OrderTypeId   = order.OrderTypeId,
            Status        = order.Status,
            Priority      = order.Priority,
            AssignedTo    = assignedTo ?? order.AssignedTo,
            UpdatedAt     = DateTime.UtcNow,
            ScheduledAt   = order.ScheduledAt,
            CompletedAt   = order.CompletedAt,
            ParentOrderId = order.ParentOrderId,
            SelectionSpec = order.SelectionSpec,
            FeatureIds    = [.. order.Features.Select(f => f.Id)],
            Attributes    = new Dictionary<string, string>(order.Attributes),
            Features      = [.. order.Features],
        };
    }

    private async Task Close() => await OnClose.InvokeAsync();
}
