using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// The mobile sync's cross-organization access (XD01-328): which other organizations' orders a caller pulls, and whether
/// it may push one. Decided by <see cref="ServiceOrderRules"/> — the XD01-326 rule, gate and chain — over a
/// <see cref="WorkflowPrincipal"/> built from the caller's token and this host's mirrors
/// (<see cref="IMirroredCrossOrganizationEntitlements"/>), never by a rule of its own.
///
/// The principal: <see cref="WorkflowPrincipal.UserId"/> is the token's <c>oid</c> (the id this host stamps on what a
/// caller does), <see cref="WorkflowPrincipal.OrganizationId"/> and <see cref="WorkflowPrincipal.RoleNames"/> come from the
/// token, the grants of its organization and the roles it holds on other organizations from the mirrors. This host has no
/// role-to-permission table, so <see cref="WorkflowPrincipal.PermissionCodes"/> (home) is empty: a grant that requires a
/// scoped role is decided by the permissions mirrored for the resource organization, an organization-wide one by the
/// actions it lists (as <c>CrossOrgGrantRule</c> decides it everywhere).
///
/// Own-organization orders are not evaluated here: their pull and push are exactly what they were before.
/// </summary>
internal sealed class FieldServiceCrossOrganizationAccess(
    ICurrentUserAccessor currentUserAccessor,
    ITenantContext tenantContext,
    IMirroredCrossOrganizationEntitlements entitlements,
    ICrossOrganizationServiceOrderReader crossOrganizationOrders,
    ServiceOrderRules rules,
    OrderTypeRegistry? orderTypeRegistry = null)
{
    private WorkflowPrincipal? _principal;

    /// <summary>
    /// The orders of other organizations the caller may view: those of the organizations its organization's grants name,
    /// fetched through the explicit grant-aware path (row-level security still applies), each kept only when
    /// <see cref="ServiceOrderRules.Evaluate"/> allows <see cref="OrderAction.View"/>.
    /// </summary>
    public async Task<IReadOnlyList<IServiceOrder>> GetViewableOrdersOfOtherOrganizationsAsync(CancellationToken ct)
    {
        var principal = await GetPrincipalAsync(ct).ConfigureAwait(false);
        var grantingOrganizations = principal.OrgGrants
            .Select(g => Guid.TryParse(g.ResourceOrganizationId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        if (grantingOrganizations.Count == 0) return [];

        var orders = await crossOrganizationOrders.GetOfOtherOrganizationsAsync(grantingOrganizations, ct).ConfigureAwait(false);
        return [.. orders.Where(o => rules.Evaluate(principal, OrderAction.View, o).Allowed)];
    }

    /// <summary>
    /// Whether the caller may push <paramref name="pushed"/> over <paramref name="stored"/>: always for an order of its own
    /// organization (unchanged); otherwise only when the rule allows the pushed action (<see cref="PushedAction"/>) on the
    /// stored order <i>and</i> on the pushed copy under the stored organization — so a push cannot carry an order out of
    /// what the grant covers (another order type, another scope) either. A pushed copy that states yet another
    /// organization is decided as it stands, and so refused.
    /// </summary>
    public async Task<bool> MayPushAsync(IServiceOrder stored, IServiceOrder pushed, CancellationToken ct)
    {
        if (stored.OrganizationId == tenantContext.CurrentOrganizationId) return true;

        var principal = await GetPrincipalAsync(ct).ConfigureAwait(false);
        var action = PushedAction(orderTypeRegistry?.Find(stored.OrderTypeId), stored.Status, pushed.Status);
        if (!rules.Evaluate(principal, action, stored).Allowed) return false;

        IServiceOrder replacement;
        try
        {
            replacement = ServiceOrderOrganization.ForUpdate(stored, pushed);
        }
        catch (ServiceOrderOrganizationException)
        {
            replacement = pushed;
        }

        return rules.Evaluate(principal, action, replacement).Allowed;
    }

    /// <summary>
    /// The action a push performs: the one the order type's graph tags the move from <paramref name="from"/> to
    /// <paramref name="to"/> with (<see cref="WorkflowTransition.TriggerAction"/> — <c>Complete</c> for a field-work order
    /// reaching <c>Completed</c>, <c>Accept</c> for one leaving <c>Dispatched</c>, ...); for a type without a graph,
    /// <see cref="OrderAction.Complete"/> into <see cref="ServiceOrderStatus.Completed"/> and
    /// <see cref="OrderAction.Cancel"/> into <see cref="ServiceOrderStatus.Cancelled"/>; and
    /// <see cref="OrderAction.Execute"/> for anything else — recording work without a status change, or an untagged move.
    /// </summary>
    internal static OrderAction PushedAction(OrderType? orderType, string from, string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal)) return OrderAction.Execute;

        if (orderType is { States.Count: > 0 })
        {
            return orderType.Transitions
                .FirstOrDefault(t => string.Equals(t.FromStateKey, from, StringComparison.Ordinal) && string.Equals(t.ToStateKey, to, StringComparison.Ordinal))
                ?.TriggerAction ?? OrderAction.Execute;
        }

        return to switch
        {
            ServiceOrderStatus.Completed => OrderAction.Complete,
            ServiceOrderStatus.Cancelled => OrderAction.Cancel,
            _ => OrderAction.Execute,
        };
    }

    private async Task<WorkflowPrincipal> GetPrincipalAsync(CancellationToken ct)
    {
        if (_principal is not null) return _principal;

        var user = await currentUserAccessor.GetCurrentUserAsync(ct).ConfigureAwait(false);
        if (user is null || string.IsNullOrEmpty(user.ExternalObjectId))
            return _principal = WorkflowPrincipal.Anonymous;

        var mirrored = await entitlements.GetAsync(user.OrganizationId, user.ExternalObjectId, ct).ConfigureAwait(false);
        return _principal = new WorkflowPrincipal(
            UserId: user.ExternalObjectId,
            OrganizationId: user.OrganizationId == Guid.Empty ? null : user.OrganizationId.ToString(),
            RoleNames: user.ExternalRoles,
            GroupIds: [],
            PermissionCodes: [])
        {
            OrgGrants = mirrored.Grants,
            ScopedRoleNames = mirrored.ScopedRoleNames,
            ScopedPermissionCodes = mirrored.ScopedPermissionCodes,
        };
    }
}
