namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// Thrown when a service order holds no target with the given id — including a target that
/// exists on another order (XD01-315). A <see cref="KeyNotFoundException"/>, distinguishable
/// from "no such order".
/// </summary>
public sealed class OrderTargetNotFoundException : KeyNotFoundException
{
    public string? OrderId { get; }

    public string? TargetId { get; }

    public OrderTargetNotFoundException(string orderId, string targetId)
        : base($"Service order '{orderId}' has no target '{targetId}'.")
    {
        OrderId = orderId;
        TargetId = targetId;
    }

    public OrderTargetNotFoundException()
    {
    }

    public OrderTargetNotFoundException(string message) : base(message)
    {
    }

    public OrderTargetNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
