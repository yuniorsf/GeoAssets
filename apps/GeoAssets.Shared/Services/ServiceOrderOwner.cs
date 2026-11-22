using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Shared.Services;

/// <summary>
/// The organization owning a service order another organization's user sees (XD01-329): the Service Order views name it
/// when it is not the viewer's own and the viewer's cross-organization entitlements name it.
/// </summary>
public static class ServiceOrderOwner
{
    /// <summary>
    /// The name of <paramref name="order"/>'s organization when it is neither unowned nor the principal's own and
    /// <paramref name="entitlements"/> names it; null otherwise — so a viewer with no cross-organization entitlement sees
    /// no owner shown.
    /// </summary>
    public static string? ForeignOwnerName(IServiceOrder order, WorkflowPrincipal? principal, CrossOrgEntitlementsDto? entitlements)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (entitlements is null || order.OrganizationId == Guid.Empty) return null;
        if (Guid.TryParse(principal?.OrganizationId, out var own) && own == order.OrganizationId) return null;

        return entitlements.OrganizationName(order.OrganizationId);
    }
}
