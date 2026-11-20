using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;

namespace GeoAssets.NeutralOperator.Modules.Occupancy;

/// <summary>
/// This module's consumer of the kernel's <see cref="IntegrationEventTypes.AssetMutationBatchAppliedV1"/> (XD01-383): the
/// approval saga's success reply, handled by <see cref="IOccupancyApplySagaService.HandleAppliedAsync"/> — inbox-deduplicated
/// there, in the unit of work of what it changes.
/// </summary>
public sealed class OccupancyApplyAppliedHandler(IOccupancyApplySagaService sagas) : IIntegrationEventHandler<AssetBatchAppliedEvent>
{
    public Task HandleAsync(AssetBatchAppliedEvent message, IntegrationMessageContext context, CancellationToken ct = default) =>
        sagas.HandleAppliedAsync(message, context, ct);
}
