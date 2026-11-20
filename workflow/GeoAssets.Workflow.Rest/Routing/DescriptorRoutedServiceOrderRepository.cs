using System.Collections.Concurrent;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// <see cref="IServiceOrderRepository"/> over every workflow API the client reaches through the gateway (XD01-384): the
/// kernel's and each vertical's that publishes its order types' <see cref="Registry.OrderTypeDescriptor.ApiBaseRoute"/>. It
/// knows no vertical — the routes are <see cref="IOrderApiRoutes"/>'s — and adds no rule: every request is the
/// <see cref="RestServiceOrderRepository"/> request of the host that serves the order, which authorizes it as before.
/// <list type="bullet">
///   <item>Create and update go to the root of the order's own order type.</item>
///   <item>An operation on an order id (read, children, parent, dispatch, action, delete) goes to the root that serves the
///   order — <see cref="ServiceOrderRouteLocator"/>; an order no root returns is answered by the kernel, as before.</item>
///   <item>A list by order type asks that type's root only. Every other list asks each root once, in
///   <see cref="IOrderApiRoutes.GetRootsAsync"/> order (the kernel's first), and concatenates the answers in that order
///   without re-sorting them, an order listed by two roots (one host serving two routes) only once, where first listed; with
///   a single root it is that host's answer, unchanged. A root that fails fails the list: no partial list is returned.</item>
///   <item>The events are the per-root repositories' own, raised after a successful write.</item>
/// </list>
/// </summary>
public sealed class DescriptorRoutedServiceOrderRepository(
    IOrderApiRoutes routes, ClientsByRoot<RestServiceOrderRepository> repositories, ServiceOrderRouteLocator locator) : IServiceOrderRepository
{
    private readonly ConcurrentDictionary<RestServiceOrderRepository, bool> _subscribed = new();

    public event EventHandler<IServiceOrder>? OrderAdded;
    public event EventHandler<IServiceOrder>? OrderUpdated;
    public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
    public event EventHandler<string>? OrderDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var (root, order) = await locator.FindAsync(id, ct).ConfigureAwait(false);
        return order ?? await For(root).GetByIdAsync(id, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetAllAsync(ct), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetRootsAsync(ct), ct);

    public async Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default)
    {
        var root = await locator.LocateAsync(parentId, ct).ConfigureAwait(false);
        return Remembered(await For(root).GetChildrenAsync(parentId, ct).ConfigureAwait(false), root);
    }

    public async Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default)
    {
        var root = await locator.LocateAsync(childId, ct).ConfigureAwait(false);
        var parent = await For(root).GetParentAsync(childId, ct).ConfigureAwait(false);
        if (parent is not null) locator.Remember(parent.Id, root);
        return parent;
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetByStatusAsync(status, ct), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetByAssigneeAsync(userId, ct), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetByCreatorAsync(userId, ct), ct);

    public async Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default)
    {
        var root = await routes.ResolveAsync(orderTypeId, ct).ConfigureAwait(false);
        return Remembered(await For(root).GetByOrderTypeAsync(orderTypeId, ct).ConfigureAwait(false), root);
    }

    public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetByDateRangeAsync(from, toExclusive, ct), ct);

    public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(
        string targetId, DispatchTargetType targetType, CancellationToken ct = default) =>
        MergeAsync(repository => repository.GetDispatchedToAsync(targetId, targetType, ct), ct);

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var root = await routes.ResolveAsync(order.OrderTypeId, ct).ConfigureAwait(false);
        await For(root).AddAsync(order, ct).ConfigureAwait(false);
        locator.Remember(order.Id, root);
    }

    public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var root = await routes.ResolveAsync(order.OrderTypeId, ct).ConfigureAwait(false);
        await For(root).UpdateAsync(order, ct).ConfigureAwait(false);
    }

    public async Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default)
    {
        var root = await locator.LocateAsync(orderId, ct).ConfigureAwait(false);
        await For(root).AppendDispatchAsync(orderId, dispatch, ct).ConfigureAwait(false);
    }

    public async Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
    {
        var root = await locator.LocateAsync(orderId, ct).ConfigureAwait(false);
        await For(root).AppendActionAsync(orderId, entry, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var root = await locator.LocateAsync(id, ct).ConfigureAwait(false);
        await For(root).DeleteAsync(id, ct).ConfigureAwait(false);
        locator.Forget(id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<IServiceOrder>> MergeAsync(
        Func<RestServiceOrderRepository, Task<IReadOnlyList<IServiceOrder>>> read, CancellationToken ct)
    {
        var roots = await routes.GetRootsAsync(ct).ConfigureAwait(false);
        if (roots.Count == 1) return Remembered(await read(For(roots[0])).ConfigureAwait(false), roots[0]);

        var merged = new List<IServiceOrder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            foreach (var order in await read(For(root)).ConfigureAwait(false))
            {
                if (!seen.Add(order.Id)) continue;

                merged.Add(order);
                locator.Remember(order.Id, root);
            }
        }

        return merged;
    }

    private IReadOnlyList<IServiceOrder> Remembered(IReadOnlyList<IServiceOrder> orders, Uri root)
    {
        foreach (var order in orders) locator.Remember(order.Id, root);
        return orders;
    }

    /// <summary>The root's repository, its events relayed as this repository's once.</summary>
    private RestServiceOrderRepository For(Uri root)
    {
        var repository = repositories.For(root);
        if (_subscribed.TryAdd(repository, true))
        {
            repository.OrderAdded += (_, order) => OrderAdded?.Invoke(this, order);
            repository.OrderUpdated += (_, order) => OrderUpdated?.Invoke(this, order);
            repository.OrderStatusChanged += (_, change) => OrderStatusChanged?.Invoke(this, change);
            repository.OrderDeleted += (_, id) => OrderDeleted?.Invoke(this, id);
        }

        return repository;
    }
}
