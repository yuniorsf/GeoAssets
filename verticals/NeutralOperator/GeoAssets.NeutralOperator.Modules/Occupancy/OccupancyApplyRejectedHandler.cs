using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;

namespace GeoAssets.NeutralOperator.Modules.Occupancy;

/// <summary>
/// This module's consumer of the kernel's <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/> (XD01-383): the
/// approval saga's compensating reply, handled by <see cref="IOccupancyApplySagaService.HandleRejectedAsync"/> —
/// inbox-deduplicated there, in the unit of work of what it changes.
/// </summary>
public sealed class OccupancyApplyRejectedHandler(IOccupancyApplySagaService sagas) : IIntegrationEventHandler<AssetMutationBatchRejectedEvent>
{
    public Task HandleAsync(AssetMutationBatchRejectedEvent message, IntegrationMessageContext context, CancellationToken ct = default) =>
        sagas.HandleRejectedAsync(message, context, ct);
}
