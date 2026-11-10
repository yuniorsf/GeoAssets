namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Parsed shape of an inbound SAP PM work-order notification (XD01-231), independent of the wire
/// format <see cref="ISapPmOrderAdapter.ParseWorkOrderNotification"/> reads it from.
/// </summary>
/// <param name="FunctionalLocation">SAP functional location — this repo's convention maps it 1:1 onto a <c>geo_entity.id</c> (resolved via <see cref="Core.Interfaces.IGeoCoreClient.GetFeatureByIdAsync"/>).</param>
/// <param name="OrderNumber">SAP work order number.</param>
/// <param name="ShortText">Short description, becomes the spawned order's <see cref="Orders.IServiceOrder.Title"/>.</param>
/// <param name="ServiceCategory">SlaGuard matrix key — see <see cref="SlaGuard.SlaGuardEngine"/>.</param>
/// <param name="TerritorialSetting">SlaGuard matrix key — see <see cref="SlaGuard.SlaGuardEngine"/>.</param>
/// <param name="NotificationId">SAP Event Mesh's CloudEvents message id — the idempotency token this ticket's AC requires for webhook-retry deduplication.</param>
/// <param name="ReportedAtUtc">When SAP recorded the notification; the SLA deadline clock starts here, not at webhook-receipt time.</param>
public sealed record SapPmOrderSpawnSpec(
    string FunctionalLocation,
    string OrderNumber,
    string ShortText,
    string ServiceCategory,
    string TerritorialSetting,
    Guid NotificationId,
    DateTimeOffset ReportedAtUtc);
