namespace GeoAssets.Workflow.Targets;

/// <summary>
/// String state keys for the per-target work lifecycle, and the legal moves between them.
///
/// <see cref="ServiceOrderTarget.Status"/> is a plain <see cref="string"/> holding one of these
/// keys — the same modelling as <see cref="Orders.ServiceOrderStatus"/> for
/// <see cref="Orders.IServiceOrder.Status"/> — so the stored value, the wire value and the
/// <see cref="OrderTargetQuery.Status"/> filter are one and the same text. Unlike an order's
/// lifecycle, this one is fixed: an order type cannot add target states.
/// </summary>
public static class OrderTargetStatus
{
    /// <summary>Added to the order; no work started on it yet.</summary>
    public const string Pending = "Pending";

    /// <summary>Work on this target has started.</summary>
    public const string InProgress = "InProgress";

    /// <summary>Work on this target finished successfully.</summary>
    public const string Done = "Done";

    /// <summary>Work was attempted but could not be carried out on this target.</summary>
    public const string NotExecutable = "NotExecutable";

    /// <summary>Deliberately left out before any work started.</summary>
    public const string Skipped = "Skipped";

    private static readonly Dictionary<string, string[]> _allowed = new(StringComparer.Ordinal)
    {
        [Pending] = [InProgress, Skipped],
        [InProgress] = [Done, NotExecutable],
        [Done] = [],
        [NotExecutable] = [],
        [Skipped] = [],
    };

    /// <summary>Every defined status key, in lifecycle order.</summary>
    public static IReadOnlyList<string> All { get; } = [Pending, InProgress, Done, NotExecutable, Skipped];

    /// <summary>Returns true if <paramref name="status"/> is one of the keys in <see cref="All"/> (case-sensitive).</summary>
    public static bool IsDefined(string? status) => status is not null && _allowed.ContainsKey(status);

    /// <summary>
    /// Returns true if a target may move from <paramref name="from"/> to <paramref name="to"/>:
    /// <see cref="Pending"/> to <see cref="InProgress"/> or <see cref="Skipped"/>,
    /// <see cref="InProgress"/> to <see cref="Done"/> or <see cref="NotExecutable"/>, and any
    /// status to itself (a no-op write). <see cref="Done"/>, <see cref="NotExecutable"/> and
    /// <see cref="Skipped"/> are final.
    /// </summary>
    public static bool CanTransition(string from, string to)
        => string.Equals(from, to, StringComparison.Ordinal) || (from is not null && _allowed.TryGetValue(from, out var next) && next.Contains(to, StringComparer.Ordinal));
}
