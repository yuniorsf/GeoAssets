using GeoAssets.Core.Interfaces;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Tells a technician's own orders from another organization's (XD01-328): since the mobile sync also pulls the orders of
/// other organizations the technician is entitled to work on, a page showing a pulled order names its owner when it is not
/// the technician's organization.
/// </summary>
public static class ServiceOrderOwnership
{
    /// <summary>
    /// The organization that owns <paramref name="order"/> when it is not <paramref name="ownOrganizationId"/>; null for an
    /// order of the caller's own organization and for one that belongs to no organization (<see cref="Guid.Empty"/>).
    /// </summary>
    public static Guid? OtherOwnerOf(IOrgOwnedResource order, Guid ownOrganizationId)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.OrganizationId != Guid.Empty && order.OrganizationId != ownOrganizationId ? order.OrganizationId : null;
    }
}
