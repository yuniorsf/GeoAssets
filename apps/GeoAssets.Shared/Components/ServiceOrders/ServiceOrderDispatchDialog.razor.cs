using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

public partial class ServiceOrderDispatchDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<(string TargetId, DispatchTargetType TargetType, string? Note)> OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private DispatchTargetType _targetType = DispatchTargetType.User;
    private string _targetId = string.Empty;
    private string _note = string.Empty;

    private async Task Confirm()
    {
        if (string.IsNullOrWhiteSpace(_targetId)) return;

        await OnConfirm.InvokeAsync((_targetId, _targetType, string.IsNullOrWhiteSpace(_note) ? null : _note));
        _targetId = string.Empty;
        _note     = string.Empty;
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
