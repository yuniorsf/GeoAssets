using GeoAssets.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// Points the map at an order whose targets were just created from the selection (XD01-312):
/// fits the view to the targets and outlines the fence the order was selected with (none for an
/// order selected by ids). Best effort — the order exists either way, so a failure is logged and
/// never surfaces as an error of the creation.
/// </summary>
public static class CreatedOrderMapFocus
{
    /// <summary>
    /// Fits, then outlines. Returns whether the map was fitted. Cancellation propagates; any other
    /// failure of either step is logged and the other step still runs.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="focus"/> or <paramref name="logger"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is null or blank.</exception>
    public static async Task<bool> FocusAsync(IMapOrderFocus focus, string orderId, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        var fitted = false;
        try
        {
            fitted = await focus.FitToOrderAsync(orderId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not fit the map to the new order {OrderId}", orderId);
        }

        try
        {
            await focus.ShowOrderFenceOutlineAsync(orderId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not outline the fence of the new order {OrderId}", orderId);
        }

        return fitted;
    }
}
