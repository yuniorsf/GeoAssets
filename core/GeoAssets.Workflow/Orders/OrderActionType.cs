namespace GeoAssets.Workflow.Orders;

/// <summary>All actions that can be attempted on a service order.</summary>
public enum OrderActionType
{
    /// <summary>Read/open the order.</summary>
    View,

    /// <summary>Approve the order to move it to a next stage.</summary>
    Approve,

    /// <summary>Reject the order, returning it or closing it.</summary>
    Reject,

    /// <summary>Assign the order to a specific user.</summary>
    Assign,

    /// <summary>
    /// Claim ownership of an order dispatched to a group/organization/user, distinct from
    /// <see cref="Assign"/> (which is done *to* someone else, typically by a supervisor).
    /// Accept is self-initiated by the recipient: "I am claiming this order."
    /// </summary>
    Accept,

    /// <summary>Dispatch the order to a user, group, or organization.</summary>
    Dispatch,

    /// <summary>Execute field work recorded on the order.</summary>
    Execute,

    /// <summary>Mark the order as completed.</summary>
    Complete,

    /// <summary>Cancel the order.</summary>
    Cancel,

    /// <summary>Add comments or attachments to the order.</summary>
    Annotate,
}
