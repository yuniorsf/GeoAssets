namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Thrown when a fan-out dispatch is asked of an order whose type declares none
/// (<see cref="Targets.OrderTargetPolicy.FanOut"/> is null, or the type has no target policy).
/// Such an order is dispatched the ordinary way, to one named target.
/// </summary>
public sealed class OrderTypeHasNoFanOutException : InvalidOperationException
{
    public OrderTypeHasNoFanOutException(string orderTypeId, string message)
        : base(message)
    {
        OrderTypeId = orderTypeId;
    }

    public OrderTypeHasNoFanOutException()
    {
    }

    public OrderTypeHasNoFanOutException(string message) : base(message)
    {
    }

    public OrderTypeHasNoFanOutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The order type that declares no fan-out; null when not known.</summary>
    public string? OrderTypeId { get; }
}
