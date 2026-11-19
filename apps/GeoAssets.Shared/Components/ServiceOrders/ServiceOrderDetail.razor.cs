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

    /// <summary>The order's type; null when the client does not know it.</summary>
    private OrderType? OrderType => Order is null ? null : OrderTypes.Find(Order.OrderTypeId);

    private bool CanDispatch    => Order is not null && Rules.Evaluate(Principal, OrderAction.Dispatch, Order).Allowed;
    // XD01-333: assigning goes through the whole-order update, where a recipient of the order's dispatch never changes the assignee.
    private bool CanAssignToMe  => Order is not null && !string.Equals(Order.AssignedTo, Principal.UserId, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderAction.Assign, Order).Allowed && !IsRecipientOnly;
    private bool CanStart       => Order is not null && string.Equals(Order.Status, ServiceOrderStatus.Pending, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderAction.Execute, Order).Allowed;
    private bool CanComplete    => Order is not null && string.Equals(Order.Status, ServiceOrderStatus.InProgress, StringComparison.Ordinal)
                                    && Rules.Evaluate(Principal, OrderAction.Complete, Order).Allowed;
    private bool CanCancel      => Order is not null
                                    && Order.Status is ServiceOrderStatus.Draft or ServiceOrderStatus.Pending
                                                    or ServiceOrderStatus.InProgress or ServiceOrderStatus.OnHold
                                    && Rules.Evaluate(Principal, OrderAction.Cancel, Order).Allowed;
    private bool CanAnnotate    => Order is not null && Rules.Evaluate(Principal, OrderAction.Annotate, Order).Allowed;

    /// <summary>
    /// A recipient of the order's dispatch accepts it along its type's accept transition, when the
    /// type lets it (XD01-336) — e.g. an occupant accepting its shared-use notice.
    /// </summary>
    private bool CanAccept      => Order is not null && IsRecipientOnly && AcceptedStatus is not null
                                    && Rules.EvaluateAppend(Principal, Order, AcceptEntry(AcceptedStatus)).Allowed;

    /// <summary>The status accepting the order moves it to from its current one; null when its type has no such move.</summary>
    private string? AcceptedStatus => Order is null ? null
        : ServiceOrderTransitions.StatusAfter(OrderType, OrderAction.Accept, Order.Status);

    /// <summary>The viewer reaches the order only because it was dispatched to it (XD01-316).</summary>
    private bool IsRecipientOnly => Order is not null && Rules.IsDispatchRecipientOnly(Principal, Order);

    /// <summary>
    /// What every panel a vertical contributes for the order's type receives (XD01-378) — see
    /// <see cref="Services.OrderDetail.IOrderDetailPanelRegistry"/>.
    /// </summary>
    private Dictionary<string, object?> PanelParameters => new(StringComparer.Ordinal)
    {
        ["Order"] = Order,
        ["Principal"] = Principal,
        ["OnChanged"] = OnChanged,
    };

    /// <summary>
    /// What the dispatch dialog a vertical contributes for the order's type receives (XD01-378) —
    /// see <see cref="Services.OrderDetail.IOrderDetailPanelRegistry"/>.
    /// </summary>
    private Dictionary<string, object?> DispatchDialogParameters => new(StringComparer.Ordinal)
    {
        ["Visible"] = _dispatchDialogVisible,
        ["Order"] = Order,
        ["Principal"] = Principal,
        ["OnDispatched"] = EventCallback.Factory.Create(this, OnDispatched),
        ["OnCancel"] = EventCallback.Factory.Create(this, CloseDispatchDialog),
    };

    private void OpenDispatchDialog() => _dispatchDialogVisible = true;

    private void CloseDispatchDialog() => _dispatchDialogVisible = false;

    /// <summary>The dialog stays open on its result; the order behind it is reloaded with its new status and children.</summary>
    private Task OnDispatched() => OnChanged.InvokeAsync();

    private async Task ConfirmDispatch((string TargetId, DispatchTargetType TargetType, string? Note) result)
    {
        if (Order is null) return;

        await Repository.AppendDispatchAsync(Order.Id,
            new OrderDispatch(result.TargetId, result.TargetType, Principal.UserId, Clock.GetUtcNow().UtcDateTime, result.Note));

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: OrderAction.Dispatch,
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
        await RecordAction(OrderAction.Execute, ServiceOrderStatus.InProgress);
    }

    private async Task Complete()
    {
        if (Order is null) return;
        await RecordAction(OrderAction.Complete, ServiceOrderStatus.Completed);
    }

    private async Task Accept()
    {
        if (Order is null || AcceptedStatus is not { } acceptedStatus) return;
        await Repository.AppendActionAsync(Order.Id, AcceptEntry(acceptedStatus));
        await OnChanged.InvokeAsync();
    }

    private OrderActionLog AcceptEntry(string acceptedStatus) => new(
        Action: OrderAction.Accept,
        PerformedBy: Principal.UserId,
        PerformedAt: Clock.GetUtcNow().UtcDateTime,
        ResultingStatus: acceptedStatus);

    private async Task CancelOrder()
    {
        if (Order is null) return;
        await RecordAction(OrderAction.Cancel, ServiceOrderStatus.Cancelled);
    }

    private async Task AddAnnotation()
    {
        if (Order is null || string.IsNullOrWhiteSpace(_noteText)) return;

        await Repository.AppendActionAsync(Order.Id, new OrderActionLog(
            Action: OrderAction.Annotate,
            PerformedBy: Principal.UserId,
            PerformedAt: Clock.GetUtcNow().UtcDateTime,
            Comment: _noteText));

        _noteText = string.Empty;
        await OnChanged.InvokeAsync();
    }

    private async Task RecordAction(OrderAction action, string resultingStatus)
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
