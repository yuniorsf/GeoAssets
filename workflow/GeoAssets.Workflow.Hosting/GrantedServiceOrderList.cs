using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The service-order list of a caller whose organization holds cross-organization grants (XD01-408): its own orders plus the
/// orders of the organizations those grants name, so an order reached only through a grant is listed and not just readable
/// by id.
///
/// The other organizations' orders come from the one explicit path past the tenant filter,
/// <see cref="ICrossOrganizationServiceOrderReader"/> (XD01-328): it is asked only for the organizations the caller's own
/// grants name — grants whose resource type covers service orders and that list a <c>serviceorders:</c> action — and
/// row-level security still decides which rows come back. Every order of the result, the caller's own included, is then
/// kept by <see cref="ServiceOrderRules.PassesOrganizationGate"/>: the same decision the by-id read makes
/// (<see cref="ServiceOrderRouteAccess.FindReadableAsync"/>), so each grant's order types, scope, expiry and scoped-role
/// requirement narrow the list exactly as they narrow that read, and the list never holds an order the read would refuse
/// nor misses one the read returns through a grant.
///
/// The result is ordered by <see cref="IServiceOrder.CreatedAt"/>, as the store orders the caller's own list, with an
/// order listed once even if both paths return it. Without a reader (a host that did not register one — a vertical host
/// today) the list is the caller's own, exactly as before.
/// </summary>
public static class GrantedServiceOrderList
{
    private const string ServiceOrderActionPrefix = "serviceorders:";

    /// <summary>
    /// The organizations the principal's grants open service orders of: a grant naming a real organization other than the
    /// principal's own, for every resource type or service orders, that lists at least one service-order action.
    /// </summary>
    public static IReadOnlySet<Guid> GrantingOrganizations(WorkflowPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var own = ServiceOrderRouteAccess.OrganizationOf(principal);
        return principal.OrgGrants
            .Where(g => OrganizationGrantMatching.CoversResourceType(g.ResourceType, nameof(ServiceOrder))
                     && g.AllowedActions.Any(a => a.StartsWith(ServiceOrderActionPrefix, StringComparison.OrdinalIgnoreCase)))
            .Select(g => Guid.TryParse(g.ResourceOrganizationId, out var organizationId) ? organizationId : Guid.Empty)
            .Where(organizationId => organizationId != Guid.Empty && organizationId != own)
            .ToHashSet();
    }

    /// <summary>
    /// <paramref name="own"/> and, when <paramref name="crossOrganization"/> is given, the orders of the organizations the
    /// principal's grants name — each kept only when the principal passes the organization gate on it — ordered by
    /// creation time.
    /// </summary>
    public static async Task<IReadOnlyList<IServiceOrder>> ComposeAsync(
        IReadOnlyList<IServiceOrder> own,
        ICrossOrganizationServiceOrderReader? crossOrganization,
        ServiceOrderRules rules,
        WorkflowPrincipal principal,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(principal);

        var granting = crossOrganization is null ? new HashSet<Guid>() : GrantingOrganizations(principal);
        if (crossOrganization is null || granting.Count == 0)
            return [.. own.Where(o => rules.PassesOrganizationGate(principal, o))];

        var granted = await crossOrganization.GetOfOtherOrganizationsAsync([.. granting], ct).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. own.Concat(granted)
            .Where(o => seen.Add(o.Id) && rules.PassesOrganizationGate(principal, o))
            .OrderBy(o => o.CreatedAt)];
    }
}
