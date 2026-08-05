namespace GeoAssets.Workflow.Orders;

/// <summary>
/// String state keys for the built-in (default) service-order lifecycle, used by
/// order types that don't define their own <see cref="OrderType.States"/>/
/// <see cref="OrderType.Transitions"/> graph (see <see cref="ServiceOrderTransitions"/>).
///
/// <see cref="IServiceOrder.Status"/> is a plain <see cref="string"/> state key —
/// not this type — so a custom <see cref="OrderType"/> can introduce states that
/// don't appear here without a code change or redeploy. These constants exist so
/// existing call sites (<c>ServiceOrderStatus.Draft</c>, etc.) keep working
/// unchanged after that migration.
/// </summary>
public static class ServiceOrderStatus
{
    /// <summary>Created but not yet submitted for execution.</summary>
    public const string Draft = "Draft";

    /// <summary>Submitted and waiting to be picked up.</summary>
    public const string Pending = "Pending";

    /// <summary>Actively being executed in the field or in a process.</summary>
    public const string InProgress = "InProgress";

    /// <summary>Temporarily suspended, waiting for an external condition.</summary>
    public const string OnHold = "OnHold";

    /// <summary>All work has been completed successfully.</summary>
    public const string Completed = "Completed";

    /// <summary>Cancelled before completion.</summary>
    public const string Cancelled = "Cancelled";
}
