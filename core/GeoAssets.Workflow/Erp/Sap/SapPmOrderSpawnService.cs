using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;

namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Spawns a <see cref="ServiceOrder"/> from an inbound SAP PM work-order notification (XD01-231),
/// deduplicated on <see cref="SapPmOrderSpawnSpec.NotificationId"/> via <see cref="IIdempotentInbox"/>
/// (this ticket's AC2 "idempotency-token deduplication on webhook retry"), resolving
/// <see cref="SapPmOrderSpawnSpec.FunctionalLocation"/> to a <c>geo_entity</c> through
/// <see cref="IGeoCoreClient"/>, and attaching the statutory SLA deadline via
/// <see cref="SlaGuardEngine"/> before persisting.
/// </summary>
public sealed class SapPmOrderSpawnService(
    IServiceOrderWriter serviceOrders,
    IGeoCoreClient geoCore,
    IIdempotentInbox idempotentInbox,
    SlaGuardEngine slaGuard,
    TimeProvider timeProvider)
{
    private const string ConsumerName = "SapPmWorkOrderWebhook";
    private const string MessageType = "SapPmWorkOrderNotification";
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    public async Task<SapPmOrderSpawnResult> SpawnFromNotificationAsync(
        SapPmOrderSpawnSpec spec, string orderTypeId, Guid organizationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(orderTypeId);

        if (await idempotentInbox.HasProcessedAsync(spec.NotificationId, ConsumerName, ct).ConfigureAwait(false))
            return new SapPmOrderSpawnResult(Order: null, AlreadyProcessed: true);

        // Resolve-then-persist-then-record, deliberately in that order: recording the
        // idempotency token only after a fully successful spawn means a webhook retry following
        // a FunctionalLocation-resolution failure (e.g. SAP sent a notification before the
        // geo_entity existed, or it existed under the wrong id) can still succeed once the
        // underlying data is fixed, instead of being permanently stuck "already processed" for a
        // notification that never actually spawned anything. This narrows, but doesn't fully
        // close, the race between two truly concurrent deliveries of the same NotificationId — a
        // storage-agnostic IServiceOrderWriter/IIdempotentInbox pair (this service deliberately
        // has no direct EF dependency, unlike InspectionCommitService) can't share one atomic
        // transaction the way a single EF-backed consumer could.
        var feature = await geoCore.GetFeatureByIdAsync(spec.FunctionalLocation, ct).ConfigureAwait(false)
            ?? throw new SapPmFunctionalLocationNotFoundException(spec.FunctionalLocation);

        var order = new ServiceOrder
        {
            OrderTypeId = orderTypeId,
            Title = spec.ShortText,
            Status = ServiceOrderStatus.Pending,
            CreatedBy = "sap-pm-webhook",
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
            OrganizationId = organizationId,
            FeatureIds = [feature.Id],
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [SapPmOrderAttributes.FunctionalLocationKey] = spec.FunctionalLocation,
                [SapPmOrderAttributes.OrderNumberKey] = spec.OrderNumber,
                [SapPmOrderAttributes.NotificationIdKey] = spec.NotificationId.ToString(),
            },
        };

        slaGuard.AttachDeadline(order, spec.ServiceCategory, spec.TerritorialSetting, timeProvider);

        await serviceOrders.AddAsync(order, ct).ConfigureAwait(false);
        await idempotentInbox.TryRecordProcessedAsync(spec.NotificationId, ConsumerName, MessageType, RetentionPeriod, ct).ConfigureAwait(false);
        return new SapPmOrderSpawnResult(order, AlreadyProcessed: false);
    }
}
