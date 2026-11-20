using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// <see cref="IOrderTypeRepository"/> over every workflow API the client reaches through the gateway (XD01-384): an order
/// type is read from, and written to, the root its descriptor names (<see cref="IOrderApiRoutes.ResolveAsync"/>, the kernel's
/// when none does). <see cref="GetAllAsync"/> asks each root once, in <see cref="IOrderApiRoutes.GetRootsAsync"/> order, and
/// keeps each type once: the version of the root its descriptor names, otherwise the first root's — so a client still learns
/// a vertical's order types once that vertical runs in its own host and the kernel no longer registers them.
/// </summary>
public sealed class DescriptorRoutedOrderTypeRepository(IOrderApiRoutes routes, ClientsByRoot<RestOrderTypeRepository> repositories)
    : IOrderTypeRepository
{
    public async Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var root = await routes.ResolveAsync(id, ct).ConfigureAwait(false);
        return await repositories.For(root).GetByIdAsync(id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default)
    {
        var roots = await routes.GetRootsAsync(ct).ConfigureAwait(false);
        if (roots.Count == 1) return await repositories.For(roots[0]).GetAllAsync(ct).ConfigureAwait(false);

        var merged = new List<OrderType>();
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            foreach (var orderType in await repositories.For(root).GetAllAsync(ct).ConfigureAwait(false))
            {
                if (!positions.TryGetValue(orderType.Id, out var position))
                {
                    positions[orderType.Id] = merged.Count;
                    merged.Add(orderType);
                }
                else if (root != routes.DefaultRoot && await routes.ResolveAsync(orderType.Id, ct).ConfigureAwait(false) == root)
                {
                    merged[position] = orderType;
                }
            }
        }

        return merged;
    }

    public async Task AddAsync(OrderType orderType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);

        var root = await routes.ResolveAsync(orderType.Id, ct).ConfigureAwait(false);
        await repositories.For(root).AddAsync(orderType, ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OrderType orderType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderType);

        var root = await routes.ResolveAsync(orderType.Id, ct).ConfigureAwait(false);
        await repositories.For(root).UpdateAsync(orderType, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var root = await routes.ResolveAsync(id, ct).ConfigureAwait(false);
        await repositories.For(root).DeleteAsync(id, ct).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
