namespace GeoAssets.Workflow.Orders;

/// <summary>Lifecycle states of a service order.</summary>
public enum ServiceOrderStatus
{
    /// <summary>Created but not yet submitted for execution.</summary>
    Draft,

    /// <summary>Submitted and waiting to be picked up.</summary>
    Pending,

    /// <summary>Actively being executed in the field or in a process.</summary>
    InProgress,

    /// <summary>Temporarily suspended, waiting for an external condition.</summary>
    OnHold,

    /// <summary>All work has been completed successfully.</summary>
    Completed,

    /// <summary>Cancelled before completion.</summary>
    Cancelled
}
