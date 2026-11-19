namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Thrown by <see cref="IOrderOccupancyStagingService.StageAsync"/> when the changes are
/// malformed or do not apply to the pole as it is now (XD01-315). One type for every such
/// reason — the precise one is the message and, in process, the inner exception — so a remote
/// client can raise the same exception the local service does.
/// </summary>
public sealed class InvalidOccupancyChangeException : ArgumentException
{
    public InvalidOccupancyChangeException()
    {
    }

    public InvalidOccupancyChangeException(string message) : base(message)
    {
    }

    public InvalidOccupancyChangeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
