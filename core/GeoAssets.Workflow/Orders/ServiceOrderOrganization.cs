namespace GeoAssets.Workflow.Orders;

/// <summary>
/// The two rules about which organization a service order is under (XD01-332), in one place so
/// every host and every repository decorator applies the same ones.
///
/// <para><b>Creation.</b> An order is created under its creator's own organization and no
/// other. An order that states none is stamped with the creator's; one that states another is
/// refused.</para>
///
/// <para><b>Update.</b> <see cref="GeoAssets.Core.Interfaces.IOrgOwnedResource.OrganizationId"/>
/// never changes after creation. An update that states the stored organization, or states none
/// (<see cref="Guid.Empty"/> — a snapshot built without it), keeps the stored one; an update that
/// states a different one is refused. Refusing rather than silently ignoring is what makes it
/// impossible for a cross-organization grantee to relabel a granted order as its own — and to
/// believe it did. The same holds for an order stored with no organization: it cannot be
/// claimed by stating one.</para>
/// </summary>
public static class ServiceOrderOrganization
{
    /// <summary>
    /// The stable <c>code</c> a host puts on its refusal of an update that states another
    /// organization, so a remote client can turn it back into
    /// <see cref="ServiceOrderOrganizationException"/>.
    /// </summary>
    public const string ChangeRefusedCode = "orders.organization-fixed";

    /// <summary>
    /// The order to store for a creation by a member of <paramref name="creatorOrganizationId"/>
    /// (<see cref="Guid.Empty"/> when the creator has none): <paramref name="order"/> itself
    /// when it already states that organization, or a copy stamped with it when it states none.
    /// </summary>
    /// <exception cref="ServiceOrderOrganizationException">The order states another organization.</exception>
    public static ServiceOrder ForCreation(ServiceOrder order, Guid creatorOrganizationId)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.OrganizationId == creatorOrganizationId) return order;
        if (order.OrganizationId == Guid.Empty) return order.CopyWithOrganization(creatorOrganizationId);

        throw new ServiceOrderOrganizationException(
            order.Id, "A service order can only be created under the creator's own organization.");
    }

    /// <summary>
    /// The order to store for an update of <paramref name="existing"/> with
    /// <paramref name="incoming"/>: <paramref name="incoming"/> itself when it states the stored
    /// organization, or a copy carrying the stored one when it states none.
    /// </summary>
    /// <exception cref="ServiceOrderOrganizationException">
    /// <paramref name="incoming"/> states a different organization, or states none and cannot be
    /// copied (it is not a <see cref="ServiceOrder"/>).
    /// </exception>
    public static IServiceOrder ForUpdate(IServiceOrder existing, IServiceOrder incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        if (incoming.OrganizationId == existing.OrganizationId) return incoming;
        if (incoming.OrganizationId == Guid.Empty && incoming is ServiceOrder order)
            return order.CopyWithOrganization(existing.OrganizationId);

        throw new ServiceOrderOrganizationException(
            incoming.Id, "A service order's organization cannot be changed after the order is created.");
    }
}
