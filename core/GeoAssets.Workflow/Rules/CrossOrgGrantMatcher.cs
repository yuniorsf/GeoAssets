using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rules;

/// <summary>
/// The one definition of "a cross-organization grant applies to this order for this
/// principal", shared by the allow-contributing cross-organization grant rule (XD01-22) and by
/// the organization gate (XD01-332) so the two can never drift apart.
///
/// A grant applies when all of these hold: it names the order's owning organization; its
/// resource type is null or <c>ServiceOrder</c>; its order types are empty or include the
/// order's <see cref="IServiceOrder.OrderTypeId"/> (ignoring case, XD01-326); its required role
/// is null or held by the principal — at home, or on the order's organization for a grant with
/// <see cref="WorkflowOrgGrant.RequiresScopedRole"/>; its scope is null or equals the order's own
/// <see cref="IScopeableResource.ScopeId"/> exactly (case-sensitive); and it has not expired.
/// Inactive grants never reach a principal: the host that builds it resolves active ones only.
///
/// The two callers then ask different questions of an applying grant.
/// <see cref="CoversAction"/> — the rule — asks whether it lists the action's permission code
/// (<c>"serviceorders:{action}"</c>, compared ignoring case), and so grants that one action.
/// <see cref="CoversOrder"/> — the gate — asks only whether it lists any service-order action
/// at all, and so lets the principal's organization reach the order; what the principal may
/// then do is the rule chain's decision, not the gate's. A grant that lists no
/// <c>serviceorders:</c> action (an empty one, or one for another kind of resource) opens nothing.
///
/// A grant with <see cref="WorkflowOrgGrant.RequiresScopedRole"/> (XD01-326) lists an action
/// for the principal only when the principal also holds that action's code on the order's
/// organization (<see cref="WorkflowPrincipal.HasPermissionOn"/>): the grant is the ceiling, the
/// roles held there decide within it, and home permissions count for nothing.
/// </summary>
internal static class CrossOrgGrantMatcher
{
    private const string ServiceOrderActionPrefix = "serviceorders:";

    /// <summary>True when a grant applying to the order lists <paramref name="action"/>.</summary>
    public static bool CoversAction(WorkflowPrincipal principal, OrderAction action, IServiceOrder order, DateTime utcNow)
    {
        // Compared ignoring case below, so the code needs no lowercasing here.
        var actionCode = $"{ServiceOrderActionPrefix}{action}";

        return Applying(principal, order, utcNow)
            .Any(g => OrganizationGrantMatching.AllowsAction(g.AllowedActions, actionCode)
                   && Qualifies(principal, g, order, actionCode));
    }

    /// <summary>True when a grant applying to the order lists at least one service-order action.</summary>
    public static bool CoversOrder(WorkflowPrincipal principal, IServiceOrder order, DateTime utcNow) =>
        Applying(principal, order, utcNow).Any(g => OpensOrder(principal, g, order));

    /// <summary>
    /// True when the grants that let the principal reach the order all require a scoped role (XD01-326): at least one
    /// such grant covers the order (<see cref="CoversOrder"/>) and no organization-wide one does. Such a principal is
    /// decided by what it holds on the order's organization, never by its home roles.
    /// </summary>
    public static bool CoversOrderOnlyThroughScopedRole(WorkflowPrincipal principal, IServiceOrder order, DateTime utcNow)
    {
        var opening = Applying(principal, order, utcNow).Where(g => OpensOrder(principal, g, order)).ToList();
        return opening.Count > 0 && opening.TrueForAll(g => g.RequiresScopedRole);
    }

    private static bool OpensOrder(WorkflowPrincipal principal, WorkflowOrgGrant grant, IServiceOrder order) =>
        grant.AllowedActions.Any(a =>
            a.StartsWith(ServiceOrderActionPrefix, StringComparison.OrdinalIgnoreCase) && Qualifies(principal, grant, order, a));

    /// <summary>
    /// Whether the principal may use <paramref name="actionCode"/> of an applying grant: always for an organization-wide
    /// grant (its home roles decide the rest, as before XD01-326); for one that requires a scoped role, only when it holds
    /// the code on the order's organization.
    /// </summary>
    private static bool Qualifies(WorkflowPrincipal principal, WorkflowOrgGrant grant, IServiceOrder order, string actionCode) =>
        !grant.RequiresScopedRole || principal.HasPermissionOn(order.OrganizationId, actionCode);

    private static IEnumerable<WorkflowOrgGrant> Applying(WorkflowPrincipal principal, IServiceOrder order, DateTime utcNow)
    {
        var resourceOrganizationId = order.OrganizationId.ToString();
        var orderScopeId = (order as IScopeableResource)?.ScopeId;

        return principal.OrgGrants.Where(g =>
            g.ResourceOrganizationId.Equals(resourceOrganizationId, StringComparison.OrdinalIgnoreCase) &&
            OrganizationGrantMatching.CoversResourceType(g.ResourceType, nameof(ServiceOrder)) &&
            OrganizationGrantMatching.CoversOrderType(g.OrderTypeIds, order.OrderTypeId) &&
            HoldsRequiredRole(principal, g, order) &&
            OrganizationGrantMatching.CoversScope(g.ScopeId, orderScopeId) &&
            (g.ExpiresAt is not { } expiresAt || expiresAt > utcNow));
    }

    private static bool HoldsRequiredRole(WorkflowPrincipal principal, WorkflowOrgGrant grant, IServiceOrder order) =>
        grant.RequiredRole is not { } role
        || (grant.RequiresScopedRole ? principal.HasRoleOn(order.OrganizationId, role) : principal.HasRole(role));
}
