namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Thrown when changes are staged for an order target that was already released — its order
/// closed — so nothing could ever commit them (XD01-315). Nothing was written.
/// </summary>
public sealed class OccupancyTargetReleasedException : InvalidOperationException
{
    /// <summary>The order the target belongs to; null when not known.</summary>
    public string? OrderId { get; }

    /// <summary>The released target; null when not known.</summary>
    public string? TargetId { get; }

    public OccupancyTargetReleasedException(string orderId, string targetId)
        : base($"Target '{targetId}' of order '{orderId}' was released when its order closed; occupancy changes can no longer be staged for it.")
    {
        OrderId = orderId;
        TargetId = targetId;
    }

    public OccupancyTargetReleasedException()
    {
    }

    public OccupancyTargetReleasedException(string message) : base(message)
    {
    }

    public OccupancyTargetReleasedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
