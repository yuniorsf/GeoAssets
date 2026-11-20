using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>The NeutralOperator side of the approval saga (XD01-383): the kernel's replies, and an operator's retry.</summary>
public interface IOccupancyApplySagaService
{
    /// <summary>The kernel applied a batch: a pending saga commits and its order moves to its approved status.</summary>
    Task<OccupancyApplySagaResult> HandleAppliedAsync(AssetBatchAppliedEvent applied, IntegrationMessageContext context, CancellationToken ct);

    /// <summary>The kernel rejected a batch: a pending saga reverts its poles and its order stays where it was, with the reason logged.</summary>
    Task<OccupancyApplySagaResult> HandleRejectedAsync(AssetMutationBatchRejectedEvent rejected, IntegrationMessageContext context, CancellationToken ct);

    /// <summary>Re-stages the batch of the order's pending saga, unchanged (same batch id), for <paramref name="principal"/>.</summary>
    Task<OccupancyApplySagaView> RetryAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct);
}
