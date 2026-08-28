using System.ComponentModel.DataAnnotations;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

public partial class ServiceOrderCreateForm
{
    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;
    [Parameter] public EventCallback<IServiceOrder> OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private readonly NewOrderModel _model = new();
    private List<OrderType> _allowedTypes = [];

    protected override void OnParametersSet()
    {
        _allowedTypes = [.. OrderTypes.All.Where(t => Rules.CanCreate(Principal, t))];
        if (string.IsNullOrEmpty(_model.OrderTypeId) && _allowedTypes.Count > 0)
            _model.OrderTypeId = _allowedTypes[0].Id;
    }

    private async Task HandleSave()
    {
        var order = new ServiceOrder
        {
            Title       = _model.Title,
            Description = _model.Description,
            OrderTypeId = _model.OrderTypeId,
            Priority    = _model.Priority,
            CreatedBy   = Principal.UserId,
        };

        await Repository.AddAsync(order);
        await OnSave.InvokeAsync(order);
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();

    private sealed class NewOrderModel
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [Required]
        public string OrderTypeId { get; set; } = string.Empty;

        public ServiceOrderPriority Priority { get; set; } = ServiceOrderPriority.Normal;
    }
}
