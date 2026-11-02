using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// One batched push item (XD01-236): the locally-modified <see cref="Order"/> snapshot plus the
/// <see cref="IdempotencyKey"/> the mobile outbox assigned it at enqueue time
/// (<c>LocalOutboxEntry.Id</c>, client-side) — lets the server recognize and skip re-applying an
/// entry a reconnect burst replays, instead of a bare <c>ServiceOrder</c> list with no way to
/// tell "this is the same push retried" from "this is a genuinely new edit".
/// </summary>
internal sealed record PushServiceOrderItem(ServiceOrder Order, string IdempotencyKey);
