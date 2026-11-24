using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services.SharedUse;

/// <summary>
/// The order list of an organization that also receives orders (XD01-316): its own orders plus
/// the orders other organizations dispatched to it — the notices an occupant of someone else's
/// poles gets. The store narrows the ordinary lists to the caller's own organization, so the
/// received ones are asked for separately and merged here.
/// </summary>
public static class ServiceOrderInbox
{
    /// <summary>
    /// The organization to ask the store about: the principal's own, when it has one that is a
    /// real organization id. Null means there is no inbox to ask for.
    /// </summary>
    public static string? OrganizationOf(WorkflowPrincipal? principal) =>
        Guid.TryParse(principal?.OrganizationId, out var organizationId) && organizationId != Guid.Empty
            ? organizationId.ToString("D")
            : null;

    /// <summary>
    /// <paramref name="own"/> followed by the orders of <paramref name="received"/> not already
    /// in it. An order dispatched inside the organization is in both; it is listed once.
    /// </summary>
    public static List<IServiceOrder> Merge(IEnumerable<IServiceOrder> own, IEnumerable<IServiceOrder> received)
    {
        ArgumentNullException.ThrowIfNull(own);
        ArgumentNullException.ThrowIfNull(received);

        var merged = new List<IServiceOrder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in own.Concat(received))
        {
            if (seen.Add(order.Id)) merged.Add(order);
        }

        return merged;
    }

    /// <summary>
    /// Whether <paramref name="order"/> was dispatched to the principal by another organization: it belongs to an
    /// organization other than the principal's and carries a dispatch to the principal's organization or to the
    /// principal. An order of another organization the principal reaches only through an organization grant is not
    /// received (XD01-410).
    /// </summary>
    public static bool IsReceived(IServiceOrder order, WorkflowPrincipal? principal)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (principal is null
            || order.OrganizationId == Guid.Empty
            || !Guid.TryParse(principal.OrganizationId, out var organizationId)
            || organizationId == order.OrganizationId)
        {
            return false;
        }

        return order.Dispatches.Any(dispatch => dispatch.TargetType switch
        {
            DispatchTargetType.Organization => Guid.TryParse(dispatch.TargetId, out var target) && target == organizationId,
            DispatchTargetType.User => string.Equals(dispatch.TargetId, principal.UserId, StringComparison.Ordinal),
            _ => false,
        });
    }
}
