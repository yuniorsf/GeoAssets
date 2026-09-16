using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Shared;

public partial class ConfirmDialog
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public string Title { get; set; } = string.Empty;
    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string ConfirmLabel { get; set; } = string.Empty;
    [Parameter] public string ConfirmButtonClass { get; set; } = "btn-danger";
    [Parameter] public EventCallback OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>
    /// Optional third action (XD01-146's Save/Discard/Cancel close guard) — rendered between
    /// Cancel and Confirm only when non-empty, so every existing 2-button caller (e.g.
    /// <c>MapWorkspace</c>'s delete confirmation) is unaffected.
    /// </summary>
    [Parameter] public string? SecondaryLabel { get; set; }
    [Parameter] public string SecondaryButtonClass { get; set; } = "btn-outline";
    [Parameter] public EventCallback OnSecondary { get; set; }

    private async Task Confirm() => await OnConfirm.InvokeAsync();
    private async Task Secondary() => await OnSecondary.InvokeAsync();
    private async Task Cancel() => await OnCancel.InvokeAsync();
    private async Task OnBackdropClick() => await OnCancel.InvokeAsync();
}
