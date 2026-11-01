namespace GeoAssets.FieldService.Server;

/// <summary>Per-order outcome of a mobile sync push (XD01-208).</summary>
internal sealed record PushServiceOrderResult(string Id, bool Success, string? Error);
