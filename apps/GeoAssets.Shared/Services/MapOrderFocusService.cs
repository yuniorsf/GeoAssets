using GeoAssets.Shared.Interfaces;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services;

/// <summary>
/// <see cref="IMapOrderFocus"/> over the existing ports: the extent comes from
/// <see cref="IServiceOrderTargetReader.GetExtentAsync"/>, the fence from the order's
/// <see cref="ServiceOrder.SelectionSpec"/>, and the map is reached through
/// <see cref="IMapInterop"/>/<see cref="IMapSelectionInterop"/> on <see cref="ICurrentMapContext"/>'s div.
/// </summary>
public sealed class MapOrderFocusService(
    IServiceOrderReader orders,
    IServiceOrderTargetReader targets,
    IMapInterop map,
    IMapSelectionInterop overlay,
    ICurrentMapContext mapContext,
    ILogger<MapOrderFocusService> logger) : IMapOrderFocus
{
    public async Task<bool> FitToExtentAsync(OrderTargetExtent? extent)
    {
        if (MapExtentFitter.ToFitBounds(extent) is not { } bbox) return false;

        await map.FitBoundsAsync(mapContext.MapDivId, bbox);
        return true;
    }

    public async Task<bool> FitToOrderAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var extent = await targets.GetExtentAsync(orderId, ct);
        ct.ThrowIfCancellationRequested();
        return await FitToExtentAsync(extent);
    }

    public async Task<bool> ShowFenceOutlineAsync(FeatureSelectionSpec? spec)
    {
        var outline = SelectionFenceOutline.Read(spec);
        if (outline.Kind == SelectionFenceOutlineKind.Drawable)
        {
            // Showing replaces whatever outline the map already has.
            await overlay.ShowSelectionOutlineAsync(mapContext.MapDivId, outline.Rings);
            return true;
        }

        if (outline.Kind == SelectionFenceOutlineKind.Malformed)
            logger.LogWarning("Selection fence outline not shown — {Problem}", outline.Problem);

        await overlay.ClearSelectionOutlineAsync(mapContext.MapDivId);
        return false;
    }

    public async Task<bool> ShowOrderFenceOutlineAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var order = await orders.GetByIdAsync(orderId, ct);
        ct.ThrowIfCancellationRequested();
        return await ShowFenceOutlineAsync(order?.SelectionSpec);
    }

    public Task ClearFenceOutlineAsync() => overlay.ClearSelectionOutlineAsync(mapContext.MapDivId);
}
