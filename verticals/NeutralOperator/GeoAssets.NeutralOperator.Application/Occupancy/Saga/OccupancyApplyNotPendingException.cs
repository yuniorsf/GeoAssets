namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>A retry of an order's asset-mutation apply refused because the order has no open saga (XD01-383).</summary>
public sealed class OccupancyApplyNotPendingException : InvalidOperationException
{
    public OccupancyApplyNotPendingException(string orderId, OccupancyApplySagaState? state)
        : base(state is null
            ? $"Service order '{orderId}' has no asset-mutation apply to retry."
            : $"Service order '{orderId}''s asset-mutation apply is {state}; only a pending one can be retried.")
    {
        OrderId = orderId;
        State = state;
    }

    public OccupancyApplyNotPendingException()
    {
        OrderId = string.Empty;
    }

    public OccupancyApplyNotPendingException(string message)
        : base(message)
    {
        OrderId = string.Empty;
    }

    public OccupancyApplyNotPendingException(string message, Exception innerException)
        : base(message, innerException)
    {
        OrderId = string.Empty;
    }

    public string OrderId { get; }

    /// <summary>The order's latest saga's state; null when it has none.</summary>
    public OccupancyApplySagaState? State { get; }
}
