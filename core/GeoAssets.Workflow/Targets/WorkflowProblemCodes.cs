namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The stable <c>code</c> values carried by the <c>application/problem+json</c> bodies of the
/// order-target and selection routes. They are the contract between a host that reports a
/// failure and a remote client that turns it back into the exception the in-process service
/// throws, which is why they live beside the port rather than in either adapter.
/// </summary>
public static class WorkflowProblemCodes
{
    /// <summary>A request body, query value or edited field is missing or malformed.</summary>
    public const string ValidationFailed = "validation.failed";

    /// <summary>A selection strategy rejected its parameters, including an invalid fence.</summary>
    public const string InvalidSelectionParameters = "selection.invalid-parameters";

    /// <summary>The selection strategy id is not registered.</summary>
    public const string UnknownSelectionStrategy = "selection.unknown-strategy";

    /// <summary>The selection matched more features than the host ceiling allows.</summary>
    public const string SelectionTooLarge = "selection.too-large";

    /// <summary><c>page</c> or <c>pageSize</c> is outside its range.</summary>
    public const string PagingOutOfRange = "paging.out-of-range";

    public const string OrderNotFound = "orders.not-found";

    /// <summary>The order's type is not registered on the host.</summary>
    public const string OrderTypeNotFound = "order-types.not-found";

    /// <summary>The order's type declares no target policy.</summary>
    public const string NoTargetPolicy = "orders.no-target-policy";

    /// <summary>The order is in a state that no longer accepts the change.</summary>
    public const string OrderInvalidState = "orders.invalid-state";

    public const string TargetNotFound = "targets.not-found";

    /// <summary>A target or its order was changed by someone else since it was read.</summary>
    public const string TargetConcurrentModification = "targets.concurrent-modification";

    /// <summary>The selection does not fit under the order type's selection limit.</summary>
    public const string TargetLimitExceeded = "targets.limit-exceeded";

    /// <summary>The order rules refused the caller.</summary>
    public const string Denied = "auth.denied";

    /// <summary>
    /// A vertical host could not reach the kernel's global target ledger, or the kernel does not know the vertical's order
    /// type yet (its registration is pending) — retry later (503, XD01-382).
    /// </summary>
    public const string TargetLedgerUnavailable = "targets.ledger-unavailable";
}
