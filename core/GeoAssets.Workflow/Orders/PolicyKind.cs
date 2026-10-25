namespace GeoAssets.Workflow.Orders;

/// <summary>The dimension along which a policy requirement is expressed.</summary>
public enum PolicyKind
{
    /// <summary>Match by role name (e.g. "Supervisor").</summary>
    Role,

    /// <summary>Match by permission code (e.g. "serviceorders:approve").</summary>
    Permission,

    /// <summary>Match by group ID.</summary>
    Group,

    /// <summary>Match by organization ID.</summary>
    Organization,
}
