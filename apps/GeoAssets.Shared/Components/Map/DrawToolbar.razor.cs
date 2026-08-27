using GeoAssets.Core.Models.Geometry;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

public partial class DrawToolbar
{
    [Parameter] public string MapDivId { get; set; } = "geoassets-map";
    [Parameter] public EventCallback<GeometryType?> OnDrawModeChanged { get; set; }

    private GeometryType? _active;

    private async Task ToggleMode(GeometryType mode)
    {
        if (_active == mode)
        {
            await Cancel();
            return;
        }

        _active = mode;
        await MapInterop.EnableDrawModeAsync(MapDivId, mode);
        await OnDrawModeChanged.InvokeAsync(mode);
    }

    private async Task Cancel()
    {
        _active = null;
        await MapInterop.DisableDrawModeAsync(MapDivId);
        await OnDrawModeChanged.InvokeAsync(null);
    }

    public void ResetMode()
    {
        _active = null;
        StateHasChanged();
    }
}
