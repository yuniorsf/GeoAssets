using System.Collections.Concurrent;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// Which workflow API root serves an order the client knows only by id (XD01-384): the root it was last read from
/// (<see cref="Remember"/> — every list and read through <see cref="DescriptorRoutedServiceOrderRepository"/> records it),
/// otherwise the first root, in <see cref="IOrderApiRoutes.GetRootsAsync"/> order (the kernel's first), whose host returns the
/// order. An order no root returns — unknown, or outside the caller's organization: the hosts answer both 404 — is placed on
/// <see cref="IOrderApiRoutes.DefaultRoot"/>, whose host then answers the operation exactly as it did before the extraction.
/// Any other failure of a host propagates. The memory holds at most <see cref="MaxRemembered"/> orders.
/// </summary>
public sealed class ServiceOrderRouteLocator(IOrderApiRoutes routes, ClientsByRoot<RestServiceOrderRepository> repositories)
{
    /// <summary>The most order ids remembered at once; the memory starts over when it is full.</summary>
    public const int MaxRemembered = 10_000;

    private readonly ConcurrentDictionary<string, Uri> _roots = new(StringComparer.Ordinal);

    public void Remember(string orderId, Uri root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(root);

        if (_roots.Count >= MaxRemembered && !_roots.ContainsKey(orderId)) _roots.Clear();
        _roots[orderId] = root;
    }

    public void Forget(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        _roots.TryRemove(orderId, out _);
    }

    /// <summary>The root serving <paramref name="orderId"/>.</summary>
    public async Task<Uri> LocateAsync(string orderId, CancellationToken ct) =>
        (await FindAsync(orderId, ct).ConfigureAwait(false)).Root;

    /// <summary>
    /// The root serving <paramref name="orderId"/> and, when finding it meant reading the order, the order as read (null
    /// otherwise, and for an order no root returns).
    /// </summary>
    public async Task<(Uri Root, IServiceOrder? Order)> FindAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        if (_roots.TryGetValue(orderId, out var known)) return (known, null);

        var roots = await routes.GetRootsAsync(ct).ConfigureAwait(false);
        if (roots.Count == 1) return (roots[0], null);

        foreach (var root in roots)
        {
            var order = await repositories.For(root).GetByIdAsync(orderId, ct).ConfigureAwait(false);
            if (order is null) continue;

            Remember(orderId, root);
            return (root, order);
        }

        return (routes.DefaultRoot, null);
    }
}
