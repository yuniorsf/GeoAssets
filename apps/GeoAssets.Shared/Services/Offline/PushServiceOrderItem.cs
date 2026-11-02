using GeoAssets.Workflow.Orders;

namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Client-side mirror of <c>GeoAssets.FieldService.Server</c>'s internal
/// <c>PushServiceOrderItem(ServiceOrder Order, string IdempotencyKey)</c> (XD01-236) — property
/// names match exactly for the same camelCase round-trip reason as <see cref="RemoteSyncPushResult"/>.
/// </summary>
public sealed record PushServiceOrderItem(ServiceOrder Order, string IdempotencyKey);
