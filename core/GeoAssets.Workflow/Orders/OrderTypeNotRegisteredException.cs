namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown when an operation needs an <see cref="OrderType"/> the <see cref="OrderTypeRegistry"/>
/// does not hold — the type of an existing order, or the child type a fan-out policy names. A
/// <see cref="KeyNotFoundException"/>, so a caller that only tells "found" from "not found"
/// needs no change; <see cref="OrderTypeId"/> tells a host which lookup failed.
/// </summary>
public sealed class OrderTypeNotRegisteredException : KeyNotFoundException
{
    public OrderTypeNotRegisteredException(string orderTypeId, string message)
        : base(message)
    {
        OrderTypeId = orderTypeId;
    }

    public OrderTypeNotRegisteredException()
    {
    }

    public OrderTypeNotRegisteredException(string message) : base(message)
    {
    }

    public OrderTypeNotRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The id that is not registered; null when not known.</summary>
    public string? OrderTypeId { get; }
}
