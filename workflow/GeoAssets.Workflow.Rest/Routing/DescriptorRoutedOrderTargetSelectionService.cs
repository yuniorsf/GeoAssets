using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// <see cref="IOrderTargetSelectionService"/> over every workflow API the client reaches through the gateway (XD01-384): a
/// selection preview or apply is the <see cref="RestOrderTargetSelectionService"/> request of the host that serves the order.
/// </summary>
public sealed class DescriptorRoutedOrderTargetSelectionService(
    ClientsByRoot<RestOrderTargetSelectionService> selections, ServiceOrderRouteLocator locator) : IOrderTargetSelectionService
{
    public async Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(request, ct).ConfigureAwait(false)).PreviewAsync(request, principal, ct).ConfigureAwait(false);

    public async Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct) =>
        await (await ForAsync(request, ct).ConfigureAwait(false)).ApplyAsync(request, principal, ct).ConfigureAwait(false);

    private async Task<RestOrderTargetSelectionService> ForAsync(TargetSelectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId, nameof(request));
        return selections.For(await locator.LocateAsync(request.OrderId, ct).ConfigureAwait(false));
    }
}
