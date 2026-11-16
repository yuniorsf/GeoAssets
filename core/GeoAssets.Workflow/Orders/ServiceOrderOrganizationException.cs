namespace GeoAssets.Workflow.Orders;

/// <summary>
/// Thrown when a write would put a service order under an organization it may not be under
/// (XD01-332): an update that states a different organization than the stored one — an order's
/// organization is fixed when it is created — or a creation for an organization other than the
/// creator's own. See <see cref="ServiceOrderOrganization"/> for the rules.
/// </summary>
public sealed class ServiceOrderOrganizationException : Exception
{
    /// <summary>The order the write was refused on; null when not known.</summary>
    public string? OrderId { get; }

    public ServiceOrderOrganizationException(string orderId, string message) : base(message)
    {
        OrderId = orderId;
    }

    public ServiceOrderOrganizationException()
    {
    }

    public ServiceOrderOrganizationException(string message) : base(message)
    {
    }

    public ServiceOrderOrganizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
