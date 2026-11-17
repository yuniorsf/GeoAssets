using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.ServiceOrders;

/// <summary>
/// The preview panel of a selection for an order (XD01-312): previews on arrival, shows the
/// server's counts and the paged targets that would be added (with their conflict badges),
/// requires an explicit acceptance when the order type's limit cuts the selection, and applies.
/// The decisions live in <see cref="OrderTargetSelectionSession"/>; this renders them.
/// </summary>
public partial class OrderTargetSelectionPanel
{
    private readonly CancellationTokenSource _lifetime = new();
    private OrderTargetSelectionSession _session = null!;
    private string? _sessionOrderId;

    /// <summary>The order the selection is for; it must already exist on the server.</summary>
    [Parameter, EditorRequired] public IServiceOrder Order { get; set; } = null!;

    [Parameter, EditorRequired] public WorkflowPrincipal Principal { get; set; } = null!;

    /// <summary>The planned strategy run (<see cref="OrderTargetSelectionPlanner"/>).</summary>
    [Parameter, EditorRequired] public OrderTargetSelectionPlan Plan { get; set; } = null!;

    /// <summary>How many features are selected on the map, for the summary.</summary>
    [Parameter] public int SelectedCount { get; set; }

    /// <summary>Raised once the selection was applied.</summary>
    [Parameter] public EventCallback<TargetSelectionResult> OnApplied { get; set; }

    /// <summary>Raised when the user leaves without applying; the order stays, without these targets.</summary>
    [Parameter] public EventCallback OnSkipped { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Order.Id, _sessionOrderId, StringComparison.Ordinal)) return;

        _sessionOrderId = Order.Id;
        _session = new OrderTargetSelectionSession(Selection, Order.Id, Plan, Principal);
        await _session.PreviewAsync(1, _lifetime.Token);
    }

    private Task PreviewAsync(int page) => _session.PreviewAsync(page, _lifetime.Token);

    private void OnTruncationAcceptedChanged(ChangeEventArgs e) => _session.SetTruncationAccepted(e.Value is true);

    private async Task ApplyAsync()
    {
        if (await _session.ApplyAsync(_lifetime.Token) is { } result)
            await OnApplied.InvokeAsync(result);
    }

    private Task SkipAsync() => OnSkipped.InvokeAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        }

        base.Dispose(disposing);
    }
}
