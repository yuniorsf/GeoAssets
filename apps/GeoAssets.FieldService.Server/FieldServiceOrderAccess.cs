using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// The one way a route of this host reaches an existing service order by id (XD01-332).
///
/// <c>GeoAssets.Server</c> gates an order through <c>ServiceOrderRules</c>' organization gate
/// (own organization, cross-organization grant, or dispatch recipient). This host wires neither
/// the rule engine nor the identity data a principal is built from, so it applies a narrower
/// rule of its own instead of a looser one: <b>the order must belong to the caller's own
/// tenant</b> (<see cref="ITenantContext.CurrentOrganizationId"/>, read from the token, never
/// from the request). A grantee or a dispatch recipient of another organization's order is not
/// served here. <c>EFServiceOrderRepository.GetByIdAsync</c> ignores the tenant query filter,
/// so without this check any order id would resolve.
///
/// An order of another tenant is reported exactly as an order that does not exist — the same
/// "404, never 403" answer <c>GeoAssets.Server</c> gives a caller outside the organization.
///
/// The mobile sync routes are the one exception (XD01-328): they also serve another
/// organization's orders the caller is entitled to under a cross-organization grant, decided by
/// <c>ServiceOrderRules</c> over the mirrored grants and scoped roles — see
/// <see cref="FieldServiceCrossOrganizationAccess"/>. Every route that uses this class stays
/// own-tenant only.
/// </summary>
internal static class FieldServiceOrderAccess
{
    /// <summary>The order with this id when it belongs to the caller's tenant; null otherwise.</summary>
    public static async Task<IServiceOrder?> FindOwnAsync(
        IServiceOrderReader orders, ITenantContext tenantContext, string orderId, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(orderId, ct).ConfigureAwait(false);
        return order is not null && order.OrganizationId == tenantContext.CurrentOrganizationId ? order : null;
    }
}
