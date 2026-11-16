namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Host-level ceilings on feature selection. They protect the host, and are independent of any
/// order type's <see cref="OrderTargetPolicy.SelectionLimit"/> (a business rule).
/// </summary>
public sealed class OrderTargetSelectionOptions
{
    public const int DefaultMaxMatched = 50_000;
    public const int DefaultMaxResolved = 10_000;

    /// <summary>
    /// The most features one preview or apply may match. A selection matching more is rejected
    /// with <see cref="SelectionTooLargeException"/>, never truncated. Must be greater than zero.
    /// </summary>
    public int MaxMatched
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxMatched;

    /// <summary>
    /// The most feature ids one map-gesture resolve may return; it is further capped by
    /// <see cref="MaxMatched"/>. Must be greater than zero.
    /// </summary>
    public int MaxResolved
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = DefaultMaxResolved;
}
