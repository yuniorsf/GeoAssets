using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// The mandatory organization gate on an existing service order (XD01-332), evaluated by
/// <see cref="ServiceOrderRules.Evaluate"/> before any rule of the chain — the service-order
/// counterpart of the gate <c>GeoFeature</c>/<c>AssetType</c> have had since XD01-21.
///
/// A principal passes when at least one holds:
/// <list type="number">
/// <item>its organization is the order's <see cref="GeoAssets.Core.Interfaces.IOrgOwnedResource.OrganizationId"/>;</item>
/// <item>its organization holds a cross-organization grant covering the order — exactly what
/// <see cref="CrossOrgGrantMatcher.CoversOrder"/> defines: resource type, scope, required role
/// and expiry included, and at least one service-order action listed;</item>
/// <item>the order was dispatched to the principal — to its user id, to a group it belongs to,
/// or to its organization: the same recipient notion the dispatch-recipient rule uses.</item>
/// </list>
/// Passing grants nothing: the rule chain still decides the action, so the gate does not depend
/// on which action is asked for. Failing denies whatever the principal's role. The principal's <see cref="ActorKind"/> plays no part — an agent or system
/// principal is held to the same three conditions as a person.
///
/// An order with no organization (<see cref="Guid.Empty"/>, created before organizations were
/// stamped) belongs to nobody, so conditions 1 and 2 cannot hold for it. Whether such an order
/// stays reachable is the host's choice, <see cref="ServiceOrderRulesOptions.AllowOrdersWithoutOrganization"/>:
/// when true (the default) every principal passes the gate on it, as before this gate existed;
/// when false only its dispatch recipients do.
/// </summary>
internal static class OrderOrganizationGate
{
    /// <summary>The relationships that make a principal a recipient of one of the order's dispatches.</summary>
    internal const OrderUserRelationship RecipientFlags =
        OrderUserRelationship.DirectRecipient |
        OrderUserRelationship.GroupMember     |
        OrderUserRelationship.OrgMember;

    public static bool Passes(RuleEvaluationContext context, bool allowOrdersWithoutOrganization, DateTime utcNow)
    {
        if ((context.Relationship & RecipientFlags) != OrderUserRelationship.None)
            return true;

        var owner = context.Order.OrganizationId;
        if (owner == Guid.Empty)
            return allowOrdersWithoutOrganization;

        return IsMember(context.Principal, owner)
            || CrossOrgGrantMatcher.CoversOrder(context.Principal, context.Order, utcNow);
    }

    /// <summary>
    /// True when the principal's organization is <paramref name="owner"/>. Compared as GUIDs, so
    /// the text form the host happened to use does not matter; a principal with no organization,
    /// or one that is not a GUID, belongs to no order's organization.
    /// </summary>
    private static bool IsMember(WorkflowPrincipal principal, Guid owner) =>
        Guid.TryParse(principal.OrganizationId, out var organizationId) && organizationId == owner;
}
