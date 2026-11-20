using GeoAssets.Core.Models;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// The order-target ports over every workflow API the client reaches through the gateway (XD01-384): each operation is the
/// <see cref="RestServiceOrderTargetRepository"/> request of the host that serves the order (<see cref="ServiceOrderRouteLocator"/>);
/// the usages of features, asked on behalf of an order, go to that order's host.
/// </summary>
public sealed class DescriptorRoutedServiceOrderTargets(
    ClientsByRoot<RestServiceOrderTargetRepository> targets, ServiceOrderRouteLocator locator) : IServiceOrderTargetReader, IServiceOrderTargetWriter
{
    public async Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await (await ForAsync(query.OrderId, ct).ConfigureAwait(false)).GetPageAsync(query, ct).ConfigureAwait(false);
    }

    public async Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).GetByIdAsync(orderId, targetId, ct).ConfigureAwait(false);

    public async Task<int> CountAsync(string orderId, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).CountAsync(orderId, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(
        IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) =>
        await (await ForAsync(excludingOrderId, ct).ConfigureAwait(false))
            .FindOpenUsagesAsync(featureIds, excludingOrderId, ct).ConfigureAwait(false);

    public async Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).GetExtentAsync(orderId, ct).ConfigureAwait(false);

    public async Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).AddRangeAsync(orderId, targets, ct).ConfigureAwait(false);

    public async Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        await (await ForAsync(target.OrderId, ct).ConfigureAwait(false)).UpdateAsync(target, ct).ConfigureAwait(false);
    }

    public async Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).RemoveAsync(orderId, targetIds, ct).ConfigureAwait(false);

    private async Task<RestServiceOrderTargetRepository> ForAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return targets.For(await locator.LocateAsync(orderId, ct).ConfigureAwait(false));
    }
}
