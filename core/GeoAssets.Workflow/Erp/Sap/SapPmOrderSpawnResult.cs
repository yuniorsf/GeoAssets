using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Result of <see cref="SapPmOrderSpawnService.SpawnFromNotificationAsync"/>. <see cref="Order"/>
/// is always the resolved order — either freshly spawned, or (when <see cref="AlreadyProcessed"/>
/// is true) null, since a deduplicated retry does not re-read the original order back out of the
/// idempotent inbox, only that one was already recorded.
/// </summary>
public sealed record SapPmOrderSpawnResult(ServiceOrder? Order, bool AlreadyProcessed);
