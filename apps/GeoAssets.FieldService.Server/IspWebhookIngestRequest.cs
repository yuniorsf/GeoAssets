namespace GeoAssets.FieldService.Server;

/// <summary>
/// Shell payload for the ISP vertical webhook ingestion endpoint (XD01-279) — the generic
/// envelope <see cref="IspModuleRestApiExtensions.MapIspModuleApi"/>'s shell validates before any
/// provider-specific dispatch exists. XD01-280 (BSS/OSS bi-directional webhook integration)
/// replaces the shell's accept-and-acknowledge body with real per-<see cref="ProviderId"/>
/// routing (IXC Soft, MK-Auth, SGP, Voalle) without changing this envelope shape.
/// </summary>
internal sealed record IspWebhookIngestRequest(string ProviderId, string EventType, string PayloadJson);
