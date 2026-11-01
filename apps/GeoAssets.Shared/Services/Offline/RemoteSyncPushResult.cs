namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Client-side mirror of <c>GeoAssets.FieldService.Server</c>'s internal
/// <c>PushServiceOrderResult(string Id, bool Success, string? Error)</c> (XD01-208) — property
/// names match exactly so <see cref="GeoAssets.Core.Services.GeoJsonSerializer.Options"/>'
/// camelCase policy round-trips without a custom converter.
/// </summary>
public sealed record RemoteSyncPushResult(string Id, bool Success, string? Error);
