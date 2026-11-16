namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Thrown by <see cref="IOrderTargetSelectionService.ApplyAsync"/> when adding the selection
/// would take the order past its type's <see cref="OrderTargetPolicy.SelectionLimit"/> and the
/// request did not accept truncation. Nothing was written.
/// </summary>
public sealed class SelectionLimitExceededException : InvalidOperationException
{
    /// <summary>The order type's selection limit.</summary>
    public int Limit { get; }

    /// <summary>How many targets the order already holds.</summary>
    public int Current { get; }

    /// <summary>How many new targets the selection asked to add.</summary>
    public int Requested { get; }

    public SelectionLimitExceededException(int limit, int current, int requested)
        : base($"The selection would add {requested} targets to an order that holds {current} of at most {limit}.")
    {
        Limit = limit;
        Current = current;
        Requested = requested;
    }

    public SelectionLimitExceededException()
    {
    }

    public SelectionLimitExceededException(string message) : base(message)
    {
    }

    public SelectionLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
