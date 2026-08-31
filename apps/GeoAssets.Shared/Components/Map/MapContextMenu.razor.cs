using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

public partial class MapContextMenu
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    [Parameter] public bool ShowSave { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }
    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private async Task HandleEdit()   { await OnEdit.InvokeAsync();   await OnClose.InvokeAsync(); }
    private async Task HandleSave()   { await OnSave.InvokeAsync();   await OnClose.InvokeAsync(); }
    private async Task HandleDelete() { await OnDelete.InvokeAsync(); await OnClose.InvokeAsync(); }
    private async Task HandleClose()  { await OnClose.InvokeAsync(); }
}
