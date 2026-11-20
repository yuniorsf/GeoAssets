using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Rest.Routing;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Rest.Routing;

/// <summary>
/// <see cref="IOrderOccupancyStagingService"/> over every workflow API the client reaches through the gateway (XD01-384):
/// each operation is the <see cref="RestOrderOccupancyStagingService"/> request of the host that serves the order — keyed by
/// the order, never by a vertical's name.
/// </summary>
public sealed class DescriptorRoutedOrderOccupancyStagingService(
    ClientsByRoot<RestOrderOccupancyStagingService> staging, ServiceOrderRouteLocator locator) : IOrderOccupancyStagingService
{
    public async Task<TargetOccupancyView> GetAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).GetAsync(orderId, targetId, principal, ct).ConfigureAwait(false);

    public async Task<PagedResult<OccupancyChangeSetView>> GetPageAsync(
        string orderId, int page, int pageSize, OccupancyChangeSetStatus? status, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false))
            .GetPageAsync(orderId, page, pageSize, status, principal, ct).ConfigureAwait(false);

    public async Task<TargetOccupancyView> StageAsync(
        string orderId, string targetId, IReadOnlyCollection<OccupancyChange> changes, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false))
            .StageAsync(orderId, targetId, changes, principal, ct).ConfigureAwait(false);

    public async Task<bool> DiscardAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).DiscardAsync(orderId, targetId, principal, ct).ConfigureAwait(false);

    public async Task<int> DiscardAllAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).DiscardAllAsync(orderId, principal, ct).ConfigureAwait(false);

    private async Task<RestOrderOccupancyStagingService> ForAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return staging.For(await locator.LocateAsync(orderId, ct).ConfigureAwait(false));
    }
}
