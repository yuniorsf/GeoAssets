using System.Text.Json;
using GeoAssets.Shared.Services.FeatureActions;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Components.Map;

public partial class MapContextMenu
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    [Parameter] public bool ShowSave { get; set; }

    /// <summary>The right-clicked feature, whose kernel feature actions the menu lists (XD01-370).</summary>
    [Parameter] public string? FeatureId { get; set; }

    [Parameter] public EventCallback OnEdit { get; set; }
    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>A feature action the user chose; the menu closes after it.</summary>
    [Parameter] public EventCallback<FeatureActionDescriptor> OnFeatureAction { get; set; }

    [Inject] private IFeatureActionsClient FeatureActions { get; set; } = null!;
    [Inject] private ILogger<MapContextMenu> Logger { get; set; } = null!;

    private FeatureActionsState _actionsState = FeatureActionsState.Idle;
    private IReadOnlyList<FeatureActionDescriptor> _actions = [];
    private string? _loadedFor;
    private CancellationTokenSource? _loading;

    /// <summary>
    /// Loads the feature's actions each time the menu opens on a feature. The base items stay usable meanwhile and after a
    /// failure, which only shows a muted "unavailable" line.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        if (!Visible || string.IsNullOrEmpty(FeatureId))
        {
            CancelLoading();
            _loadedFor = null;
            _actionsState = FeatureActionsState.Idle;
            _actions = [];
            return;
        }

        if (string.Equals(_loadedFor, FeatureId, StringComparison.Ordinal)) return;

        _loadedFor = FeatureId;
        _actions = [];
        _actionsState = FeatureActionsState.Loading;
        CancelLoading();
        var loading = _loading = new CancellationTokenSource();

        try
        {
            _actions = await FeatureActions.GetActionsAsync(FeatureId, loading.Token);
            _actionsState = FeatureActionsState.Loaded;
        }
        catch (OperationCanceledException) when (loading.IsCancellationRequested)
        {
            // The menu closed or moved to another feature; that load owns the state now.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or NotSupportedException)
        {
            LogActionsUnavailable(Logger, FeatureId, ex);
            _actionsState = FeatureActionsState.Failed;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CancelLoading();
        base.Dispose(disposing);
    }

    private void CancelLoading()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
    }

    private async Task HandleEdit()   { await OnEdit.InvokeAsync();   await OnClose.InvokeAsync(); }
    private async Task HandleSave()   { await OnSave.InvokeAsync();   await OnClose.InvokeAsync(); }
    private async Task HandleDelete() { await OnDelete.InvokeAsync(); await OnClose.InvokeAsync(); }
    private Task HandleClose()  => OnClose.InvokeAsync();

    private async Task HandleFeatureAction(FeatureActionDescriptor action)
    {
        await OnFeatureAction.InvokeAsync(action);
        await OnClose.InvokeAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The feature actions of {FeatureId} could not be loaded; the context menu shows its base items only")]
    private static partial void LogActionsUnavailable(ILogger logger, string featureId, Exception ex);
}
