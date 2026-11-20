using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// The shared-use dispatch ports over every workflow API the client reaches through the gateway (XD01-384): each operation
/// is the <see cref="RestSharedUseDispatchClient"/> request of the host that serves the order — keyed by the order, never by
/// a vertical's name.
/// </summary>
public sealed class DescriptorRoutedSharedUseDispatchClient(
    ClientsByRoot<RestSharedUseDispatchClient> dispatch, ServiceOrderRouteLocator locator) : ISharedUseDispatchService, ISharedUseDispatchPlanReader
{
    public async Task<SharedUseDispatchPreview> PreviewAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false)).PreviewAsync(orderId, page, pageSize, principal, ct).ConfigureAwait(false);

    public async Task<PagedResult<SharedUseSkippedEntry>> GetSkippedEntriesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false))
            .GetSkippedEntriesAsync(orderId, page, pageSize, principal, ct).ConfigureAwait(false);

    public async Task<PagedResult<string>> GetUnnotifiablePolesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false))
            .GetUnnotifiablePolesAsync(orderId, page, pageSize, principal, ct).ConfigureAwait(false);

    public async Task<SharedUseDispatchResult> DispatchAsync(
        string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(orderId, ct).ConfigureAwait(false))
            .DispatchAsync(orderId, expectedRowVersion, principal, ct).ConfigureAwait(false);

    private async Task<RestSharedUseDispatchClient> ForAsync(string orderId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        return dispatch.For(await locator.LocateAsync(orderId, ct).ConfigureAwait(false));
    }
}
