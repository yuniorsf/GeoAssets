namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// Where a client reaches the workflow API of an order type (XD01-384, ADR-001 Phase 7): a vertical that runs in its own host
/// publishes its order types' <see cref="Registry.OrderTypeDescriptor.ApiBaseRoute"/> to the kernel's registry, and the client
/// reaches that route through the one gateway it talks to — never through a host name of its own. Each root is a workflow API
/// root (the parent of a <c>service-orders</c> collection: <c>…/service-orders</c>, <c>…/order-types</c>, the target routes),
/// as an absolute URI with a trailing slash.
/// </summary>
public interface IOrderApiRoutes
{
    /// <summary>The root of every order type no descriptor names: the kernel's workflow API.</summary>
    Uri DefaultRoot { get; }

    /// <summary>Every distinct root: <see cref="DefaultRoot"/> first, then the descriptors' roots in ordinal order.</summary>
    Task<IReadOnlyList<Uri>> GetRootsAsync(CancellationToken ct);

    /// <summary>The root of <paramref name="orderTypeId"/>'s descriptor, or <see cref="DefaultRoot"/> when none names it.</summary>
    Task<Uri> ResolveAsync(string orderTypeId, CancellationToken ct);
}
