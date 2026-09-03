namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Defines which status transitions are structurally legal, independent of who is
/// performing them — see <see cref="Rules.ServiceOrderRules"/> for the separate
/// "who is allowed" gate.
///
/// Consulted by every write path that can change <see cref="IServiceOrder.Status"/>
/// (<c>EFServiceOrderRepository</c>'s <c>UpdateAsync</c>/<c>AppendActionAsync</c>, and
/// <see cref="ServiceOrder.Transition"/>), so the workflow graph has a single source of truth
/// instead of being reimplemented ad hoc wherever a status check happens to be needed.
///
/// The two-argument <see cref="IsValid(string, string)"/> overload always validates
/// against the global default graph below. The <see cref="IsValid(OrderType?, string, string)"/>
/// overload additionally consults an <see cref="OrderType"/>'s own
/// <see cref="OrderType.States"/>/<see cref="OrderType.Transitions"/> when it defines
/// one, falling back to the global graph when it doesn't (empty = "use the default"
/// — the same convention <see cref="OrderType.CreationPolicies"/> already uses).
/// </summary>
public static class ServiceOrderTransitions
{
    private static readonly Dictionary<string, string[]> _allowed = new()
    {
        [ServiceOrderStatus.Draft]      = [ServiceOrderStatus.Pending, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.Pending]    = [ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.InProgress] = [ServiceOrderStatus.OnHold, ServiceOrderStatus.Completed, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.OnHold]     = [ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled],
        [ServiceOrderStatus.Completed]  = [],
        [ServiceOrderStatus.Cancelled]  = [],
    };

    /// <summary>
    /// Returns true if moving from <paramref name="from"/> to <paramref name="to"/> is
    /// structurally legal under the global default graph. Staying in the same status
    /// is always allowed (a no-op write). <see cref="ServiceOrderStatus.Completed"/>
    /// and <see cref="ServiceOrderStatus.Cancelled"/> are terminal — no transition out
    /// of either is legal.
    /// </summary>
    public static bool IsValid(string from, string to)
        => from == to || (_allowed.TryGetValue(from, out var next) && next.Contains(to));

    /// <summary>
    /// Same as <see cref="IsValid(string, string)"/>, but validates against
    /// <paramref name="orderType"/>'s own <see cref="OrderType.States"/>/
    /// <see cref="OrderType.Transitions"/> graph when it defines one (non-empty
    /// <see cref="OrderType.States"/>), falling back to the global default graph when
    /// it doesn't (or when <paramref name="orderType"/> is null).
    /// </summary>
    public static bool IsValid(OrderType? orderType, string from, string to)
    {
        if (from == to) return true;

        if (orderType is { States.Count: > 0 })
            return orderType.Transitions.Any(t => t.FromStateKey == from && t.ToStateKey == to);

        return IsValid(from, to);
    }

    /// <summary>
    /// Returns true if a transition tagged with <paramref name="action"/> exists from
    /// <paramref name="from"/> in <paramref name="orderType"/>'s graph. For order types
    /// with no custom graph (or none supplied), preserves the historical
    /// self-cancel-only-from-Draft-or-Pending behavior for
    /// <see cref="OrderActionType.Cancel"/> — the global default graph has no
    /// per-edge action tagging to derive this from generically.
    /// </summary>
    public static bool HasTransitionFor(OrderType? orderType, string from, OrderActionType action)
    {
        if (orderType is { States.Count: > 0 })
            return orderType.Transitions.Any(t => t.FromStateKey == from && t.TriggerAction == action);

        return action == OrderActionType.Cancel
            && from is ServiceOrderStatus.Draft or ServiceOrderStatus.Pending;
    }

    /// <summary>
    /// Returns true if <paramref name="status"/> is a success-terminal state for
    /// <paramref name="orderType"/> — i.e. reaching it should stamp
    /// <see cref="IServiceOrder.CompletedAt"/>. For order types with a custom graph,
    /// this is the <see cref="WorkflowState.IsSuccess"/> flag on the matching
    /// <see cref="WorkflowState"/>; otherwise it's the literal
    /// <see cref="ServiceOrderStatus.Completed"/> comparison every order type used
    /// before per-type graphs existed.
    /// </summary>
    public static bool IsSuccessState(OrderType? orderType, string status)
    {
        if (orderType is { States.Count: > 0 })
            return orderType.States.Any(s => s.Key == status && s.IsSuccess);

        return status == ServiceOrderStatus.Completed;
    }
}

/// <summary>
/// Thrown when a caller attempts a status transition that
/// <see cref="ServiceOrderTransitions.IsValid(string, string)"/> (or the
/// <see cref="OrderType"/>-aware overload) rejects as structurally illegal (e.g.
/// reopening a <see cref="ServiceOrderStatus.Completed"/> order, or skipping straight
/// from <see cref="ServiceOrderStatus.Draft"/> to <see cref="ServiceOrderStatus.Completed"/>).
/// </summary>
public sealed class InvalidServiceOrderTransitionException(string from, string to)
    : InvalidOperationException($"Cannot transition a service order from '{from}' to '{to}'.")
{
    public string From { get; } = from;
    public string To   { get; } = to;
}
