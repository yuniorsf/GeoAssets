using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Shared;

public partial class SearchBar
{
    [Parameter] public string Query { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> QueryChanged { get; set; }
    [Parameter] public EventCallback<string> OnSearch { get; set; }

    private System.Timers.Timer? _debounce;

    private async Task OnInput(ChangeEventArgs e)
    {
        Query = e.Value?.ToString() ?? string.Empty;
        await QueryChanged.InvokeAsync(Query);

        _debounce?.Dispose();
        _debounce = new System.Timers.Timer(300);
        _debounce.Elapsed += async (_, _) =>
        {
            _debounce?.Dispose();
            await InvokeAsync(() => OnSearch.InvokeAsync(Query));
        };
        _debounce.AutoReset = false;
        _debounce.Start();
    }

    private async Task Clear()
    {
        Query = string.Empty;
        await QueryChanged.InvokeAsync(Query);
        await OnSearch.InvokeAsync(Query);
    }

    public override void Dispose()
    {
        _debounce?.Dispose();
        base.Dispose();
    }
}
