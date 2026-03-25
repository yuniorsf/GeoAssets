namespace GeoAssets.Workflow.Rules;

/// <summary>
/// Describes how a user relates to a specific service order.
/// Used by <see cref="ServiceOrderRules"/> to determine which actions are permitted.
///
/// A user may hold multiple relationships simultaneously (e.g. both Creator and OrgMember).
/// </summary>
[Flags]
public enum OrderUserRelationship
{
    None            = 0,

    /// <summary>The user created the order.</summary>
    Creator         = 1 << 0,

    /// <summary>The order is directly assigned to the user.</summary>
    Assignee        = 1 << 1,

    /// <summary>The user initiated a dispatch on this order.</summary>
    Dispatcher      = 1 << 2,

    /// <summary>The order was dispatched to the user's organization.</summary>
    OrgMember       = 1 << 3,

    /// <summary>The order was dispatched to a group the user belongs to.</summary>
    GroupMember     = 1 << 4,

    /// <summary>The order was dispatched directly to this user.</summary>
    DirectRecipient = 1 << 5,
}
