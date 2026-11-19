namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// The role names the ISP order types' policies name (XD01-364) — seeded as built-in roles by the identity seeder, so an
/// order type's <see cref="Orders.OrderType.CreationPolicies"/> and <see cref="Orders.OrderType.ActionPermissions"/> can
/// say who may submit a request or contest a notice as data, evaluated by the one rule chain.
/// </summary>
public static class IspRoles
{
    /// <summary>A retail ISP renting space on a neutral operator's poles: submits attachment requests and disputes notices.</summary>
    public const string Tenant = "IspTenant";

    /// <summary>The neutral operator's staff that flags clandestine attachments and mediates disputes.</summary>
    public const string Supervisor = "Supervisor";

    /// <summary>Unrestricted operator staff.</summary>
    public const string Administrator = "Administrator";
}
