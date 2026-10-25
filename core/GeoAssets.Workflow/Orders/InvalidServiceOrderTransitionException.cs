namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown when a caller attempts a status transition that
/// <see cref="ServiceOrderTransitions.IsValid(string, string)"/> (or the
/// <see cref="OrderType"/>-aware overload) rejects as structurally illegal (e.g.
/// reopening a <see cref="ServiceOrderStatus.Completed"/> order, or skipping straight
/// from <see cref="ServiceOrderStatus.Draft"/> to <see cref="ServiceOrderStatus.Completed"/>).
/// </summary>
public sealed class InvalidServiceOrderTransitionException : InvalidOperationException
{
    public string? From { get; }
    public string? To   { get; }

    public InvalidServiceOrderTransitionException(string from, string to)
        : base($"Cannot transition a service order from '{from}' to '{to}'.")
    {
        From = from;
        To   = to;
    }

    public InvalidServiceOrderTransitionException()
    {
    }

    public InvalidServiceOrderTransitionException(string message) : base(message)
    {
    }

    public InvalidServiceOrderTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
