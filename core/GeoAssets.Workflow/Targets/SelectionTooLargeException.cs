namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Thrown when a selection matches more features than the host's hard ceiling
/// (<see cref="OrderTargetSelectionOptions.MaxMatched"/>, or
/// <see cref="OrderTargetSelectionOptions.MaxResolved"/> for a map-gesture resolve) allows.
/// The ceiling is independent of any order type's selection limit and is never truncated to:
/// the caller must narrow the selection.
/// </summary>
public sealed class SelectionTooLargeException : InvalidOperationException
{
    /// <summary>
    /// A lower bound on how many features matched: matching stops one past the ceiling, so the
    /// true number may be higher.
    /// </summary>
    public int MatchedAtLeast { get; }

    /// <summary>The ceiling that was exceeded.</summary>
    public int Ceiling { get; }

    public SelectionTooLargeException(int matchedAtLeast, int ceiling)
        : base($"The selection matches at least {matchedAtLeast} features, more than the {ceiling} allowed in one selection.")
    {
        MatchedAtLeast = matchedAtLeast;
        Ceiling = ceiling;
    }

    public SelectionTooLargeException()
    {
    }

    public SelectionTooLargeException(string message) : base(message)
    {
    }

    public SelectionTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
