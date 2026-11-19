using GeoAssets.Shared.Interfaces;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Tests.Components.ServiceOrders;

/// <summary>An <see cref="IMapOrderFocus"/> a render never calls; any call is a test failure.</summary>
internal sealed class UnusedMapOrderFocus : IMapOrderFocus
{
    private static NotSupportedException Unused() => new("The render must not reach the map.");

    public Task<bool> FitToExtentAsync(OrderTargetExtent? extent) => throw Unused();
    public Task<bool> FitToOrderAsync(string orderId, CancellationToken ct) => throw Unused();
    public Task<bool> ShowFenceOutlineAsync(FeatureSelectionSpec? spec) => throw Unused();
    public Task<bool> ShowOrderFenceOutlineAsync(string orderId, CancellationToken ct) => throw Unused();
    public Task ClearFenceOutlineAsync() => throw Unused();
}
