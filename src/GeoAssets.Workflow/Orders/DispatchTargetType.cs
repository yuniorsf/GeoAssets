namespace GeoAssets.Workflow.Orders;

/// <summary>The kind of entity an order was dispatched to.</summary>
public enum DispatchTargetType
{
    /// <summary>Dispatched to a specific user by their ID.</summary>
    User,

    /// <summary>Dispatched to an entire group — all members with appropriate permissions can act.</summary>
    Group,

    /// <summary>Dispatched to an entire organization.</summary>
    Organization,
}
