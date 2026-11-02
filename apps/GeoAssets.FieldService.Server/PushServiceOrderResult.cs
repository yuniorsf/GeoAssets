namespace GeoAssets.FieldService.Server;

/// <summary>
/// Per-order outcome of a mobile sync push (XD01-208). <see cref="Duplicate"/> is <c>true</c>
/// when this exact <c>(ServiceOrder.Id, IdempotencyKey)</c> pair had already been applied by an
/// earlier push and was short-circuited rather than re-applied (XD01-236) — still
/// <see cref="Success"/>, but distinguishable for callers/tests that want to confirm a replayed
/// batch didn't double-apply.
/// </summary>
internal sealed record PushServiceOrderResult(string Id, bool Success, string? Error, bool Duplicate = false);
