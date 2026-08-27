using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Shared;

public partial class ConfirmDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string ConfirmLabel { get; set; } = string.Empty;
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private async Task Confirm() => await OnConfirm.InvokeAsync();
    private async Task Cancel() => await OnCancel.InvokeAsync();
    private async Task OnBackdropClick() => await OnCancel.InvokeAsync();
}
