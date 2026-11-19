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
/// resource type is null or <c>ServiceOrder</c>; its required role is null or held by the
/// principal; its scope is null or equals the order's own
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
            .Any(g => OrganizationGrantMatching.AllowsAction(g.AllowedActions, actionCode));
    }

    /// <summary>True when a grant applying to the order lists at least one service-order action.</summary>
    public static bool CoversOrder(WorkflowPrincipal principal, IServiceOrder order, DateTime utcNow) =>
        Applying(principal, order, utcNow)
            .Any(g => g.AllowedActions.Any(a => a.StartsWith(ServiceOrderActionPrefix, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<WorkflowOrgGrant> Applying(WorkflowPrincipal principal, IServiceOrder order, DateTime utcNow)
    {
        var resourceOrganizationId = order.OrganizationId.ToString();
        var orderScopeId = (order as IScopeableResource)?.ScopeId;

        return principal.OrgGrants.Where(g =>
            g.ResourceOrganizationId.Equals(resourceOrganizationId, StringComparison.OrdinalIgnoreCase) &&
            OrganizationGrantMatching.CoversResourceType(g.ResourceType, nameof(ServiceOrder)) &&
            (g.RequiredRole is null || principal.HasRole(g.RequiredRole)) &&
            OrganizationGrantMatching.CoversScope(g.ScopeId, orderScopeId) &&
            (g.ExpiresAt is not { } expiresAt || expiresAt > utcNow));
    }
}
